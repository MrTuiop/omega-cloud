using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Auth;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Application.DTOs.Nodes;
using OmegaCloud.Application.Services;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;
using Xunit;

namespace OmegaCloud.Tests.Integration;

public class SignalRHubIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public SignalRHubIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(string AccessToken, Guid UserId, Guid SessionId)> LoginAsync(string username, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            UsernameOrEmail = username,
            Password = password,
            DeviceName = "Integration Test Device"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions);
        Assert.NotNull(body);

        // Get session id by querying sessions
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        var sessionsResponse = await client.GetAsync("/api/v1/auth/sessions");
        var sessions = await sessionsResponse.Content.ReadFromJsonAsync<List<UserSessionDto>>(_jsonOptions);
        var currentSession = sessions?.FirstOrDefault(s => s.IsCurrent);

        return (body.AccessToken, body.User.Id, currentSession?.Id ?? Guid.Empty);
    }

    private HubConnection CreateHubConnection(string? accessToken = null)
    {
        var uri = new Uri(_factory.Server.BaseAddress, "/hubs/omega");
        var builder = new HubConnectionBuilder()
            .WithUrl(uri, options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (!string.IsNullOrEmpty(accessToken))
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }
            });

        return builder.Build();
    }

    [Fact]
    public async Task Connect_WithoutToken_ShouldFailAuthentication()
    {
        // Arrange
        await using var connection = CreateHubConnection();

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await connection.StartAsync();
        });
    }

    [Fact]
    public async Task Connect_WithValidToken_ShouldSucceedAndPingPong()
    {
        // Arrange
        var (token, _, _) = await LoginAsync(
            CustomWebApplicationFactory.DefaultRegularUsername,
            CustomWebApplicationFactory.DefaultRegularPassword);

        await using var connection = CreateHubConnection(token);

        // Act
        await connection.StartAsync();

        var pong = await connection.InvokeAsync<string>("Ping");
        await connection.InvokeAsync("SubscribeToFolder", (Guid?)Guid.NewGuid());
        await connection.InvokeAsync("UnsubscribeFromFolder", (Guid?)null);

        // Assert
        Assert.Equal(HubConnectionState.Connected, connection.State);
        Assert.Equal("pong", pong);
    }

    [Fact]
    public async Task AdminConnection_ShouldReceiveNodeStatusChanged_WhenNodeStatusChanges()
    {
        // Arrange
        var (adminToken, _, _) = await LoginAsync(
            CustomWebApplicationFactory.DefaultAdminUsername,
            CustomWebApplicationFactory.DefaultAdminPassword);

        var (userToken, _, _) = await LoginAsync(
            CustomWebApplicationFactory.DefaultRegularUsername,
            CustomWebApplicationFactory.DefaultRegularPassword);

        await using var adminConn = CreateHubConnection(adminToken);
        await using var userConn = CreateHubConnection(userToken);

        var adminReceivedTcs = new TaskCompletionSource<(Guid nodeId, string status)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var userReceived = false;

        adminConn.On<Guid, string, DateTimeOffset>("NodeStatusChanged", (nodeId, status, _) =>
        {
            adminReceivedTcs.TrySetResult((nodeId, status));
        });

        userConn.On<Guid, string, DateTimeOffset>("NodeStatusChanged", (_, _, _) =>
        {
            userReceived = true;
        });

        await adminConn.StartAsync();
        await userConn.StartAsync();

        // Act: Trigger node status change via IRealtimeNotifier or INodeService
        using var scope = _factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>();
        var testNodeId = Guid.NewGuid();

        await notifier.NotifyNodeStatusChangedAsync(testNodeId, "Degraded", DateTimeOffset.UtcNow);

        // Assert
        var timeoutTask = Task.Delay(5000);
        var completed = await Task.WhenAny(adminReceivedTcs.Task, timeoutTask);
        Assert.Same(adminReceivedTcs.Task, completed);

        var result = await adminReceivedTcs.Task;
        Assert.Equal(testNodeId, result.nodeId);
        Assert.Equal("Degraded", result.status);
        Assert.False(userReceived); // Regular users must not receive admin node status changes
    }

    [Fact]
    public async Task Connection_ShouldReceiveForceLogout_WhenSessionIsRevoked()
    {
        // Arrange
        var (userToken, userId, sessionId) = await LoginAsync(
            CustomWebApplicationFactory.DefaultRegularUsername,
            CustomWebApplicationFactory.DefaultRegularPassword);

        await using var userConn = CreateHubConnection(userToken);

        var logoutReceivedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        userConn.On<string>("ForceLogout", reason =>
        {
            logoutReceivedTcs.TrySetResult(reason);
        });

        await userConn.StartAsync();

        // Act: Revoke the session via HTTP API
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        var revokeResponse = await client.DeleteAsync($"/api/v1/auth/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        // Assert
        var timeoutTask = Task.Delay(5000);
        var completed = await Task.WhenAny(logoutReceivedTcs.Task, timeoutTask);
        Assert.Same(logoutReceivedTcs.Task, completed);

        var reason = await logoutReceivedTcs.Task;
        Assert.Contains("revoked", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Connection_ShouldReceiveDirectoryChangedAndQuotaUpdated_WhenFileIsUploaded()
    {
        // Arrange
        var (userToken, userId, _) = await LoginAsync(
            CustomWebApplicationFactory.DefaultRegularUsername,
            CustomWebApplicationFactory.DefaultRegularPassword);

        await using var userConn = CreateHubConnection(userToken);

        var dirChangedTcs = new TaskCompletionSource<(Guid? folderId, string changeType)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var quotaUpdatedTcs = new TaskCompletionSource<(long used, long total)>(TaskCreationOptions.RunContinuationsAsynchronously);

        userConn.On<Guid?, string, object?>("DirectoryChanged", (folderId, changeType, _) =>
        {
            dirChangedTcs.TrySetResult((folderId, changeType));
        });

        userConn.On<long, long>("QuotaUpdated", (used, total) =>
        {
            quotaUpdatedTcs.TrySetResult((used, total));
        });

        await userConn.StartAsync();

        // Act: Upload a small file via HTTP API
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("Hello SignalR File Upload"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var fileName = $"signalr_test_{Guid.NewGuid():N}.txt";
        content.Add(fileContent, "file", fileName);

        var response = await client.PostAsync("/api/v1/files/upload", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Assert
        var timeoutTask = Task.Delay(5000);
        var completedDir = await Task.WhenAny(dirChangedTcs.Task, timeoutTask);
        Assert.Same(dirChangedTcs.Task, completedDir);

        var dirResult = await dirChangedTcs.Task;
        Assert.Equal("Created", dirResult.changeType);

        var completedQuota = await Task.WhenAny(quotaUpdatedTcs.Task, timeoutTask);
        Assert.Same(quotaUpdatedTcs.Task, completedQuota);

        var quotaResult = await quotaUpdatedTcs.Task;
        Assert.True(quotaResult.used > 0);
    }

    [Fact]
    public async Task Connection_ShouldReceiveFileProgress_WhenProgressReported()
    {
        // Arrange
        var (userToken, userId, _) = await LoginAsync(
            CustomWebApplicationFactory.DefaultRegularUsername,
            CustomWebApplicationFactory.DefaultRegularPassword);

        await using var userConn = CreateHubConnection(userToken);

        var progressTcs = new TaskCompletionSource<(Guid sessionId, double percent)>(TaskCreationOptions.RunContinuationsAsynchronously);

        userConn.On<Guid, double, double>("FileProgress", (sessId, percent, _) =>
        {
            progressTcs.TrySetResult((sessId, percent));
        });

        await userConn.StartAsync();

        // Act: Trigger FileProgress via IRealtimeNotifier
        using var scope = _factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>();
        var testSessionId = Guid.NewGuid();

        await notifier.NotifyFileProgressAsync(userId, testSessionId, 75.0, 102400.0);

        // Assert
        var timeoutTask = Task.Delay(5000);
        var completed = await Task.WhenAny(progressTcs.Task, timeoutTask);
        Assert.Same(progressTcs.Task, completed);

        var progressResult = await progressTcs.Task;
        Assert.Equal(testSessionId, progressResult.sessionId);
        Assert.Equal(75.0, progressResult.percent);
    }

    [Fact]
    public async Task Connection_ShouldReceiveFileProgress_WhenSubscribedToUploadSession()
    {
        // Arrange
        var (userToken, userId, _) = await LoginAsync(
            CustomWebApplicationFactory.DefaultRegularUsername,
            CustomWebApplicationFactory.DefaultRegularPassword);

        await using var userConn = CreateHubConnection(userToken);
        var testSessionId = Guid.NewGuid();
        var progressTcs = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);

        userConn.On<Guid, double, double>("FileProgress", (sessId, percent, _) =>
        {
            if (sessId == testSessionId)
            {
                progressTcs.TrySetResult(percent);
            }
        });

        await userConn.StartAsync();
        await userConn.InvokeAsync("SubscribeToUploadSession", testSessionId);

        // Act: Trigger FileProgress targeted at this session
        using var scope = _factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>();
        await notifier.NotifyFileProgressAsync(userId, testSessionId, 85.0, 50000.0);

        // Assert
        var timeoutTask = Task.Delay(5000);
        var completed = await Task.WhenAny(progressTcs.Task, timeoutTask);
        Assert.Same(progressTcs.Task, completed);
        Assert.Equal(85.0, await progressTcs.Task);

        await userConn.InvokeAsync("UnsubscribeFromUploadSession", testSessionId);
    }

    [Fact]
    public async Task Connection_ShouldReceiveBothFoldersDirectoryChanged_WhenFileMovedViaApi()
    {
        // Arrange
        var (userToken, userId, _) = await LoginAsync(
            CustomWebApplicationFactory.DefaultRegularUsername,
            CustomWebApplicationFactory.DefaultRegularPassword);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        // 1. Create a destination folder
        var folderRes = await client.PostAsJsonAsync("/api/v1/folders", new { name = $"Target_{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, folderRes.StatusCode);
        var folderDto = await folderRes.Content.ReadFromJsonAsync<OmegaCloud.Application.DTOs.Folders.FolderDto>(_jsonOptions);
        Assert.NotNull(folderDto);

        // 2. Upload a file to root
        using var uploadContent = new MultipartFormDataContent();
        var fileBytes = new ByteArrayContent("Moving file content"u8.ToArray());
        var fileName = $"move_test_{Guid.NewGuid():N}.txt";
        uploadContent.Add(fileBytes, "file", fileName);

        var uploadRes = await client.PostAsync("/api/v1/files/upload", uploadContent);
        Assert.Equal(HttpStatusCode.Created, uploadRes.StatusCode);
        var fileDto = await uploadRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions);
        Assert.NotNull(fileDto);

        // 3. Connect SignalR
        await using var userConn = CreateHubConnection(userToken);
        var movedEvents = new List<(Guid? folderId, string changeType)>();
        var rootMovedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var targetMovedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        userConn.On<Guid?, string, object?>("DirectoryChanged", (fId, changeType, _) =>
        {
            if (changeType == "Moved")
            {
                lock (movedEvents)
                {
                    movedEvents.Add((fId, changeType));
                }
                if (fId == null) rootMovedTcs.TrySetResult(true);
                if (fId == folderDto.Id) targetMovedTcs.TrySetResult(true);
            }
        });

        await userConn.StartAsync();

        // Act: Move file from root to folderDto.Id
        var moveRes = await client.PatchAsJsonAsync($"/api/v1/files/{fileDto.Id}", new { folderId = folderDto.Id });
        Assert.Equal(HttpStatusCode.OK, moveRes.StatusCode);

        // Assert: Both source folder (null/root) and destination folder receive "Moved"
        var timeoutTask = Task.Delay(5000);
        var allReceived = Task.WhenAll(rootMovedTcs.Task, targetMovedTcs.Task);
        var completed = await Task.WhenAny(allReceived, timeoutTask);
        Assert.Same(allReceived, completed);

        lock (movedEvents)
        {
            Assert.Contains(movedEvents, e => e.folderId == null && e.changeType == "Moved");
            Assert.Contains(movedEvents, e => e.folderId == folderDto.Id && e.changeType == "Moved");
        }
    }
}
