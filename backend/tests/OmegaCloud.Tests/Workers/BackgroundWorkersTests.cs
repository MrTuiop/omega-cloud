using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmegaCloud.Application.DTOs.Nodes;
using OmegaCloud.Application.DTOs.Trash;
using OmegaCloud.Application.DTOs.Upload;
using OmegaCloud.Application.Services;
using OmegaCloud.Infrastructure.Options;
using OmegaCloud.Infrastructure.Workers;

namespace OmegaCloud.Tests.Workers;

public class FakeOptionsMonitor<T> : IOptionsMonitor<T>
{
    public FakeOptionsMonitor(T currentValue)
    {
        CurrentValue = currentValue;
    }

    public T CurrentValue { get; set; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

public class FakeTrashService : ITrashService
{
    private readonly TaskCompletionSource _cleanupExecutedTcs = new();
    public int CleanupCallsCount { get; private set; }

    public Task WaitForCleanupExecutionAsync(TimeSpan timeout)
        => Task.WhenAny(_cleanupExecutedTcs.Task, Task.Delay(timeout));

    public Task<TrashContentDto> GetTrashContentAsync(Guid userId, bool topLevelOnly = false, CancellationToken ct = default)
        => Task.FromResult(new TrashContentDto());

    public Task<RestoreItemResultDto> RestoreItemAsync(Guid id, Guid userId, CancellationToken ct = default)
        => Task.FromResult(new RestoreItemResultDto());

    public Task PermanentlyDeleteItemAsync(Guid id, Guid userId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<EmptyTrashResultDto> EmptyTrashAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(new EmptyTrashResultDto());

    public Task<TrashCleanupResultDto> CleanupExpiredTrashAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        CleanupCallsCount++;
        _cleanupExecutedTcs.TrySetResult();
        return Task.FromResult(new TrashCleanupResultDto());
    }
}

public class FakeUploadGcService : IUploadGcService
{
    private readonly TaskCompletionSource _gcExecutedTcs = new();
    public int GcCallsCount { get; private set; }

    public Task WaitForGcExecutionAsync(TimeSpan timeout)
        => Task.WhenAny(_gcExecutedTcs.Task, Task.Delay(timeout));

    public Task<UploadGcResultDto> CleanupExpiredSessionsAsync(TimeSpan? removeSessionsOlderThan = null, CancellationToken ct = default)
    {
        GcCallsCount++;
        _gcExecutedTcs.TrySetResult();
        return Task.FromResult(new UploadGcResultDto());
    }
}

public class BackgroundWorkersTests
{
    [Fact]
    public async Task TrashCleanerWorker_RunsAndRespectsCancellation()
    {
        var fakeTrashService = new FakeTrashService();

        var services = new ServiceCollection();
        services.AddScoped<ITrashService>(_ => fakeTrashService);
        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var optionsMonitor = new FakeOptionsMonitor<TrashCleanupOptions>(new TrashCleanupOptions
        {
            Enabled = true,
            RetentionDays = 30,
            CheckIntervalMinutes = 60
        });

        var worker = new TrashCleanerWorker(
            scopeFactory,
            optionsMonitor,
            NullLogger<TrashCleanerWorker>.Instance);

        using var cts = new CancellationTokenSource();
        var startTask = worker.StartAsync(cts.Token);

        await fakeTrashService.WaitForCleanupExecutionAsync(TimeSpan.FromSeconds(3));

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        Assert.True(fakeTrashService.CleanupCallsCount >= 1);
    }

    [Fact]
    public async Task TrashCleanerWorker_WhenDisabled_DoesNotInvokeCleanup()
    {
        var fakeTrashService = new FakeTrashService();

        var services = new ServiceCollection();
        services.AddScoped<ITrashService>(_ => fakeTrashService);
        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var optionsMonitor = new FakeOptionsMonitor<TrashCleanupOptions>(new TrashCleanupOptions
        {
            Enabled = false,
            RetentionDays = 30,
            CheckIntervalMinutes = 60
        });

        var worker = new TrashCleanerWorker(
            scopeFactory,
            optionsMonitor,
            NullLogger<TrashCleanerWorker>.Instance);

        using var cts = new CancellationTokenSource();
        var startTask = worker.StartAsync(cts.Token);
        await Task.Delay(50);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, fakeTrashService.CleanupCallsCount);
    }

    [Fact]
    public async Task UploadSessionGcWorker_RunsAndRespectsCancellation()
    {
        var fakeGcService = new FakeUploadGcService();

        var services = new ServiceCollection();
        services.AddScoped<IUploadGcService>(_ => fakeGcService);
        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var optionsMonitor = new FakeOptionsMonitor<UploadGcOptions>(new UploadGcOptions
        {
            Enabled = true,
            CheckIntervalMinutes = 30,
            DeadSessionRetentionDays = 7
        });

        var worker = new UploadSessionGcWorker(
            scopeFactory,
            optionsMonitor,
            NullLogger<UploadSessionGcWorker>.Instance);

        using var cts = new CancellationTokenSource();
        var startTask = worker.StartAsync(cts.Token);

        await fakeGcService.WaitForGcExecutionAsync(TimeSpan.FromSeconds(3));

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        Assert.True(fakeGcService.GcCallsCount >= 1);
    }

    [Fact]
    public async Task UploadSessionGcWorker_WhenDisabled_DoesNotInvokeCleanup()
    {
        var fakeGcService = new FakeUploadGcService();

        var services = new ServiceCollection();
        services.AddScoped<IUploadGcService>(_ => fakeGcService);
        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var optionsMonitor = new FakeOptionsMonitor<UploadGcOptions>(new UploadGcOptions
        {
            Enabled = false,
            CheckIntervalMinutes = 30,
            DeadSessionRetentionDays = 7
        });

        var worker = new UploadSessionGcWorker(
            scopeFactory,
            optionsMonitor,
            NullLogger<UploadSessionGcWorker>.Instance);

        using var cts = new CancellationTokenSource();
        var startTask = worker.StartAsync(cts.Token);
        await Task.Delay(50);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, fakeGcService.GcCallsCount);
    }

    [Fact]
    public async Task NodeHealthWorker_RunsAndRespectsCancellation()
    {
        var fakeNodeService = new FakeNodeService();

        var services = new ServiceCollection();
        services.AddScoped<INodeService>(_ => fakeNodeService);
        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var optionsMonitor = new FakeOptionsMonitor<NodeHealthOptions>(new NodeHealthOptions
        {
            Enabled = true,
            CheckIntervalSeconds = 1,
            OfflineThresholdSeconds = 60,
            DegradedThresholdSeconds = 30
        });

        var worker = new NodeHealthWorker(
            scopeFactory,
            optionsMonitor,
            NullLogger<NodeHealthWorker>.Instance);

        using var cts = new CancellationTokenSource();
        var startTask = worker.StartAsync(cts.Token);

        await fakeNodeService.WaitForCheckExecutionAsync(TimeSpan.FromSeconds(3));

        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        Assert.True(fakeNodeService.CheckCallsCount >= 1);
    }

    [Fact]
    public async Task NodeHealthWorker_WhenDisabled_DoesNotInvokeCheck()
    {
        var fakeNodeService = new FakeNodeService();

        var services = new ServiceCollection();
        services.AddScoped<INodeService>(_ => fakeNodeService);
        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var optionsMonitor = new FakeOptionsMonitor<NodeHealthOptions>(new NodeHealthOptions
        {
            Enabled = false,
            CheckIntervalSeconds = 1,
            OfflineThresholdSeconds = 60,
            DegradedThresholdSeconds = 30
        });

        var worker = new NodeHealthWorker(
            scopeFactory,
            optionsMonitor,
            NullLogger<NodeHealthWorker>.Instance);

        using var cts = new CancellationTokenSource();
        var startTask = worker.StartAsync(cts.Token);
        await Task.Delay(50);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, fakeNodeService.CheckCallsCount);
    }
}

public class FakeNodeService : INodeService
{
    private readonly TaskCompletionSource _checkExecutedTcs = new();
    public int CheckCallsCount { get; private set; }

    public Task WaitForCheckExecutionAsync(TimeSpan timeout)
        => Task.WhenAny(_checkExecutedTcs.Task, Task.Delay(timeout));

    public Task<OfflineNodesCheckResultDto> CheckOfflineNodesAsync(TimeSpan offlineThreshold, TimeSpan degradedThreshold, CancellationToken ct = default)
    {
        CheckCallsCount++;
        _checkExecutedTcs.TrySetResult();
        return Task.FromResult(new OfflineNodesCheckResultDto());
    }

    public Task<RegisterNodeResponse> RegisterNodeAsync(OmegaCloud.Application.DTOs.Nodes.RegisterNodeRequest request, CancellationToken ct = default)
        => Task.FromResult(new RegisterNodeResponse());

    public Task<HeartbeatResponseDto> ProcessHeartbeatAsync(OmegaCloud.Application.DTOs.Nodes.NodeHeartbeatRequest request, string? nodeIdHeader, string? signatureHeader, string? timestampHeader, string? secretHeader, string? authHeader, CancellationToken ct = default)
        => Task.FromResult(new HeartbeatResponseDto());

    public Task<IReadOnlyList<OmegaCloud.Application.DTOs.Nodes.StorageNodeDto>> GetAllNodesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OmegaCloud.Application.DTOs.Nodes.StorageNodeDto>>(Array.Empty<OmegaCloud.Application.DTOs.Nodes.StorageNodeDto>());

    public Task<OmegaCloud.Application.DTOs.Nodes.StorageNodeDto> GetNodeByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(new OmegaCloud.Application.DTOs.Nodes.StorageNodeDto());

    public Task<NodeRegistrationTokenDto> GenerateRegistrationTokenAsync(OmegaCloud.Application.DTOs.Nodes.GenerateNodeTokenRequest request, CancellationToken ct = default)
        => Task.FromResult(new NodeRegistrationTokenDto());

    public Task<bool> DeleteOrArchiveNodeAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<NodePingResultDto> PingNodeAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(new NodePingResultDto { NodeId = id, IsReachable = true });

    public Task<OmegaCloud.Application.DTOs.Nodes.StorageNodeDto> UpdateNodeAsync(Guid id, OmegaCloud.Application.DTOs.Nodes.UpdateStorageNodeRequest request, CancellationToken ct = default)
        => Task.FromResult(new OmegaCloud.Application.DTOs.Nodes.StorageNodeDto { Id = id });
}
