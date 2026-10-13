using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Nodes;
using OmegaCloud.Application.Services;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;
using OmegaCloud.Infrastructure.Data;
using OmegaCloud.Infrastructure.Security;
using OmegaCloud.Infrastructure.Services;
using OmegaCloud.Infrastructure.Storage;

namespace OmegaCloud.Tests.Services;

public class NodeServiceTests
{
    private readonly HmacSigner _hmacSigner;
    private readonly DateTimeProvider _dateTimeProvider;
    private readonly RemoteNodeGateway _gateway;

    public NodeServiceTests()
    {
        _dateTimeProvider = new DateTimeProvider();
        _hmacSigner = new HmacSigner(_dateTimeProvider);
        _gateway = new RemoteNodeGateway(
            new HttpClient(),
            _hmacSigner,
            _dateTimeProvider,
            NullLogger<RemoteNodeGateway>.Instance);
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private NodeService CreateService(AppDbContext db, IDiskTelemetryProvider? diskTelemetry = null)
    {
        return new NodeService(
            db,
            _hmacSigner,
            _gateway,
            _dateTimeProvider,
            NullLogger<NodeService>.Instance,
            diskTelemetryProvider: diskTelemetry);
    }

    [Fact]
    public async Task RegisterNodeAsync_ValidRegistrationKey_RegistersSuccessfully()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var token = await service.GenerateRegistrationTokenAsync(new GenerateNodeTokenRequest
        {
            Name = "Pending-NAS",
            BaseUrl = "https://nas.test:8000",
            IsBehindNat = false
        });

        var registerRequest = new RegisterNodeRequest
        {
            RegistrationKey = token.RegistrationKey,
            Name = "My Home NAS",
            BaseUrl = "https://nas.test:9000",
            IsBehindNat = false,
            TotalSpaceBytes = 2_000_000_000_000L,
            AvailableSpaceBytes = 1_500_000_000_000L
        };

        var response = await service.RegisterNodeAsync(registerRequest);

        Assert.NotNull(response);
        Assert.Equal(token.NodeId, response.NodeId);
        Assert.NotEmpty(response.NodeSecret);
        Assert.Equal("My Home NAS", response.Name);
        Assert.Equal(StorageNodeStatus.Online.ToString(), response.Status);

        var nodeInDb = await db.StorageNodes.FindAsync(token.NodeId);
        Assert.NotNull(nodeInDb);
        Assert.Equal(response.NodeSecret, nodeInDb.NodeSecretHash);
        Assert.Equal(StorageNodeStatus.Online, nodeInDb.Status);
        Assert.Equal(2_000_000_000_000L, nodeInDb.TotalSpaceBytes);
        Assert.Equal(1_500_000_000_000L, nodeInDb.AvailableSpaceBytes);
    }

    [Fact]
    public async Task RegisterNodeAsync_InvalidOrUsedKey_ThrowsExceptions()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        // 1. Empty key
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RegisterNodeAsync(new RegisterNodeRequest { RegistrationKey = "" }));

        // 2. Unknown key
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.RegisterNodeAsync(new RegisterNodeRequest { RegistrationKey = "unknown_key_123" }));

        // 3. Already registered key
        var token = await service.GenerateRegistrationTokenAsync(new GenerateNodeTokenRequest());
        await service.RegisterNodeAsync(new RegisterNodeRequest { RegistrationKey = token.RegistrationKey });

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RegisterNodeAsync(new RegisterNodeRequest { RegistrationKey = token.RegistrationKey }));
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_ValidHmacSignature_UpdatesTelemetry()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var token = await service.GenerateRegistrationTokenAsync(new GenerateNodeTokenRequest());
        var reg = await service.RegisterNodeAsync(new RegisterNodeRequest
        {
            RegistrationKey = token.RegistrationKey,
            TotalSpaceBytes = 1_000_000_000L,
            AvailableSpaceBytes = 900_000_000L
        });

        var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signaturePayload = $"{reg.NodeId}:{unixSeconds}";
        var signature = _hmacSigner.ComputeHmacSha256(signaturePayload, reg.NodeSecret);

        var heartbeatRequest = new NodeHeartbeatRequest
        {
            TotalSpaceBytes = 1_000_000_000L,
            AvailableSpaceBytes = 850_000_000L,
            CpuLoad = 22.5,
            DiskStatus = "Healthy"
        };

        var response = await service.ProcessHeartbeatAsync(
            heartbeatRequest,
            nodeIdHeader: reg.NodeId.ToString(),
            signatureHeader: signature,
            timestampHeader: unixSeconds.ToString(),
            secretHeader: null,
            authHeader: null);

        Assert.NotNull(response);
        Assert.Equal(reg.NodeId, response.NodeId);
        Assert.Equal(StorageNodeStatus.Online.ToString(), response.Status);

        var node = await db.StorageNodes.FindAsync(reg.NodeId);
        Assert.NotNull(node);
        Assert.Equal(850_000_000L, node.AvailableSpaceBytes);
        Assert.Equal(22.5, node.CpuLoad);
        Assert.Equal("Healthy", node.DiskStatus);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_SecretHeaderOrBearerAuth_Succeeds()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var token = await service.GenerateRegistrationTokenAsync(new GenerateNodeTokenRequest());
        var reg = await service.RegisterNodeAsync(new RegisterNodeRequest
        {
            RegistrationKey = token.RegistrationKey,
            TotalSpaceBytes = 1000,
            AvailableSpaceBytes = 800
        });

        // 1. Direct X-Node-Secret header
        var resp1 = await service.ProcessHeartbeatAsync(
            new NodeHeartbeatRequest { AvailableSpaceBytes = 750 },
            nodeIdHeader: reg.NodeId.ToString(),
            signatureHeader: null,
            timestampHeader: null,
            secretHeader: reg.NodeSecret,
            authHeader: null);
        Assert.Equal(StorageNodeStatus.Online.ToString(), resp1.Status);

        // 2. Bearer Authorization header
        var resp2 = await service.ProcessHeartbeatAsync(
            new NodeHeartbeatRequest { AvailableSpaceBytes = 700 },
            nodeIdHeader: reg.NodeId.ToString(),
            signatureHeader: null,
            timestampHeader: null,
            secretHeader: null,
            authHeader: $"Bearer {reg.NodeSecret}");
        Assert.Equal(StorageNodeStatus.Online.ToString(), resp2.Status);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_InvalidAuthOrSkewedTimestamp_Fails()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var token = await service.GenerateRegistrationTokenAsync(new GenerateNodeTokenRequest());
        var reg = await service.RegisterNodeAsync(new RegisterNodeRequest
        {
            RegistrationKey = token.RegistrationKey
        });

        // Invalid signature
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.ProcessHeartbeatAsync(
                new NodeHeartbeatRequest(),
                nodeIdHeader: reg.NodeId.ToString(),
                signatureHeader: "bad_signature",
                timestampHeader: DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                secretHeader: null,
                authHeader: null));

        // Skewed timestamp (>300 seconds)
        var skewedUnix = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        var sig = _hmacSigner.ComputeHmacSha256($"{reg.NodeId}:{skewedUnix}", reg.NodeSecret);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.ProcessHeartbeatAsync(
                new NodeHeartbeatRequest(),
                nodeIdHeader: reg.NodeId.ToString(),
                signatureHeader: sig,
                timestampHeader: skewedUnix.ToString(),
                secretHeader: null,
                authHeader: null));
    }

    [Fact]
    public async Task GetAllNodesAsync_ComputesCorrectStatusAndMetrics()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var localNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Master Local",
            Type = StorageNodeType.LocalDisk,
            Status = StorageNodeStatus.Online,
            TotalSpaceBytes = 1_000_000,
            AvailableSpaceBytes = 700_000,
            LastHeartbeatAt = DateTime.UtcNow
        };
        db.StorageNodes.Add(localNode);
        await db.SaveChangesAsync();

        var nodes = await service.GetAllNodesAsync();
        Assert.NotEmpty(nodes);

        var localDto = nodes.First(n => n.Id == localNode.Id);
        Assert.Equal(300_000, localDto.UsedSpaceBytes);
        Assert.Equal(70.0, localDto.FreeSpacePercentage);
        Assert.True(localDto.IsOnline);
    }

    [Fact]
    public async Task DeleteOrArchiveNodeAsync_ProtectsMasterAndHandlesBlobs()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var localNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Master Local",
            Type = StorageNodeType.LocalDisk,
            RegistrationKey = "master-key",
            NodeSecretHash = "master-secret"
        };
        db.StorageNodes.Add(localNode);

        var remoteWithBlobs = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Remote With Blobs",
            Type = StorageNodeType.RemoteDaemon,
            RegistrationKey = "k1",
            NodeSecretHash = "s1"
        };
        db.StorageNodes.Add(remoteWithBlobs);
        db.StorageBlobs.Add(new StorageBlob
        {
            Id = Guid.NewGuid(),
            StorageNodeId = remoteWithBlobs.Id
        });

        var remoteEmpty = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Remote Empty",
            Type = StorageNodeType.RemoteDaemon,
            RegistrationKey = "k2",
            NodeSecretHash = "s2"
        };
        db.StorageNodes.Add(remoteEmpty);

        await db.SaveChangesAsync();

        // 1. Master local cannot be deleted
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.DeleteOrArchiveNodeAsync(localNode.Id));

        // 2. Node with blobs is archived (IsActive = false, Status = Offline)
        await service.DeleteOrArchiveNodeAsync(remoteWithBlobs.Id);
        var archived = await db.StorageNodes.FindAsync(remoteWithBlobs.Id);
        Assert.NotNull(archived);
        Assert.False(archived.IsActive);
        Assert.Equal(StorageNodeStatus.Offline, archived.Status);

        // 3. Node without blobs is completely removed
        await service.DeleteOrArchiveNodeAsync(remoteEmpty.Id);
        var deleted = await db.StorageNodes.FindAsync(remoteEmpty.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task CheckOfflineNodesAsync_MarksMissedHeartbeatsCorrectly()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var now = DateTime.UtcNow;

        // Fresh node (10s ago) -> Should remain Online
        var freshNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Fresh Node",
            Type = StorageNodeType.RemoteDaemon,
            Status = StorageNodeStatus.Online,
            LastHeartbeatAt = now.AddSeconds(-10),
            IsActive = true
        };

        // Degraded node (45s ago, threshold 30s) -> Should become Degraded
        var degradedNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Degraded Node",
            Type = StorageNodeType.RemoteDaemon,
            Status = StorageNodeStatus.Online,
            LastHeartbeatAt = now.AddSeconds(-45),
            IsActive = true
        };

        // Offline node (75s ago, threshold 60s) -> Should become Offline
        var offlineNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Offline Node",
            Type = StorageNodeType.RemoteDaemon,
            Status = StorageNodeStatus.Online,
            LastHeartbeatAt = now.AddSeconds(-75),
            IsActive = true
        };

        db.StorageNodes.AddRange(freshNode, degradedNode, offlineNode);
        await db.SaveChangesAsync();

        var result = await service.CheckOfflineNodesAsync(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(30));

        Assert.Equal(1, result.MarkedOfflineCount);
        Assert.Equal(1, result.MarkedDegradedCount);

        var updatedFresh = await db.StorageNodes.FindAsync(freshNode.Id);
        var updatedDegraded = await db.StorageNodes.FindAsync(degradedNode.Id);
        var updatedOffline = await db.StorageNodes.FindAsync(offlineNode.Id);

        Assert.Equal(StorageNodeStatus.Online, updatedFresh!.Status);
        Assert.Equal(StorageNodeStatus.Degraded, updatedDegraded!.Status);
        Assert.Equal(StorageNodeStatus.Offline, updatedOffline!.Status);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_DegradedNode_RecoversToOnline()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var token = await service.GenerateRegistrationTokenAsync(new GenerateNodeTokenRequest());
        var reg = await service.RegisterNodeAsync(new RegisterNodeRequest
        {
            RegistrationKey = token.RegistrationKey
        });

        // Artificially put node in Degraded status
        var node = await db.StorageNodes.FindAsync(reg.NodeId);
        node!.Status = StorageNodeStatus.Degraded;
        await db.SaveChangesAsync();

        var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = _hmacSigner.ComputeHmacSha256($"{reg.NodeId}:{unix}", reg.NodeSecret);

        // Heartbeat with no explicit status in body should recover node from Degraded to Online
        var resp = await service.ProcessHeartbeatAsync(
            new NodeHeartbeatRequest(),
            nodeIdHeader: reg.NodeId.ToString(),
            signatureHeader: sig,
            timestampHeader: unix.ToString(),
            secretHeader: null,
            authHeader: null);

        Assert.Equal(StorageNodeStatus.Online.ToString(), resp.Status);

        var updated = await db.StorageNodes.FindAsync(reg.NodeId);
        Assert.Equal(StorageNodeStatus.Online, updated!.Status);
    }

    [Fact]
    public async Task CheckOfflineNodesAsync_MaintenanceNode_IsNotMarkedOfflineOrDegraded()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var node = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Maintenance NAS",
            Type = StorageNodeType.RemoteDaemon,
            Status = StorageNodeStatus.Maintenance,
            LastHeartbeatAt = DateTime.UtcNow.AddMinutes(-10),
            IsActive = true
        };
        db.StorageNodes.Add(node);
        await db.SaveChangesAsync();

        var result = await service.CheckOfflineNodesAsync(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(30));

        Assert.Equal(0, result.MarkedOfflineCount);
        Assert.Equal(0, result.MarkedDegradedCount);

        var refreshed = await db.StorageNodes.FindAsync(node.Id);
        Assert.Equal(StorageNodeStatus.Maintenance, refreshed!.Status);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_IsoExpiredTimestamp_Rejected()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var token = await service.GenerateRegistrationTokenAsync(new GenerateNodeTokenRequest());
        var reg = await service.RegisterNodeAsync(new RegisterNodeRequest
        {
            RegistrationKey = token.RegistrationKey
        });

        // Expired ISO timestamp (10 minutes ago)
        var oldIso = DateTimeOffset.UtcNow.AddMinutes(-10).ToString("o");
        var sig = _hmacSigner.ComputeHmacSha256($"{reg.NodeId}:{oldIso}", reg.NodeSecret);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.ProcessHeartbeatAsync(
                new NodeHeartbeatRequest(),
                nodeIdHeader: reg.NodeId.ToString(),
                signatureHeader: sig,
                timestampHeader: oldIso,
                secretHeader: null,
                authHeader: null));
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_MismatchedHeaderAndBodyNodeId_ThrowsValidationException()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var token = await service.GenerateRegistrationTokenAsync(new GenerateNodeTokenRequest());
        var reg = await service.RegisterNodeAsync(new RegisterNodeRequest
        {
            RegistrationKey = token.RegistrationKey
        });

        var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = _hmacSigner.ComputeHmacSha256($"{reg.NodeId}:{unix}", reg.NodeSecret);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ProcessHeartbeatAsync(
                new NodeHeartbeatRequest { NodeId = Guid.NewGuid() },
                nodeIdHeader: reg.NodeId.ToString(),
                signatureHeader: sig,
                timestampHeader: unix.ToString(),
                secretHeader: null,
                authHeader: null));
    }

    [Fact]
    public async Task RegisterNodeAsync_InvalidSpaces_ThrowsValidationException()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var token = await service.GenerateRegistrationTokenAsync(new GenerateNodeTokenRequest());

        // Available > Total space
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RegisterNodeAsync(new RegisterNodeRequest
            {
                RegistrationKey = token.RegistrationKey,
                TotalSpaceBytes = 1000,
                AvailableSpaceBytes = 2000
            }));

        // Negative space
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RegisterNodeAsync(new RegisterNodeRequest
            {
                RegistrationKey = token.RegistrationKey,
                TotalSpaceBytes = -500,
                AvailableSpaceBytes = 0
            }));
    }

    [Fact]
    public async Task UpdateNodeAsync_UpdatesNodeSuccessfully()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var token = await service.GenerateRegistrationTokenAsync(new GenerateNodeTokenRequest { Name = "Old Name" });

        var updated = await service.UpdateNodeAsync(token.NodeId, new UpdateStorageNodeRequest
        {
            Name = "New Renamed NAS",
            BaseUrl = "https://new-nas.local:9443",
            IsBehindNat = true,
            Status = StorageNodeStatus.Maintenance
        });

        Assert.Equal("New Renamed NAS", updated.Name);
        Assert.Equal("https://new-nas.local:9443", updated.BaseUrl);
        Assert.True(updated.IsBehindNat);
        Assert.Equal(StorageNodeStatus.Maintenance.ToString(), updated.Status);

        var inDb = await db.StorageNodes.FindAsync(token.NodeId);
        Assert.Equal("New Renamed NAS", inDb!.Name);
        Assert.Equal(StorageNodeStatus.Maintenance, inDb.Status);
    }

    [Fact]
    public async Task PingNodeAsync_LocalNode_ReturnsZeroLatencyAndReachable()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var localNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Master Local",
            Type = StorageNodeType.LocalDisk,
            Status = StorageNodeStatus.Online
        };
        db.StorageNodes.Add(localNode);
        await db.SaveChangesAsync();

        var result = await service.PingNodeAsync(localNode.Id);

        Assert.NotNull(result);
        Assert.True(result.IsReachable);
        Assert.Equal(0, result.LatencyMs);
        Assert.Equal(StorageNodeStatus.Online.ToString(), result.Status);

        var inDb = await db.StorageNodes.FindAsync(localNode.Id);
        Assert.Equal(0, inDb!.LatencyMs);
    }

    private class FakeDiskTelemetryProvider : IDiskTelemetryProvider
    {
        private readonly DiskSpaceInfo _info;
        public FakeDiskTelemetryProvider(long total, long available) => _info = new DiskSpaceInfo(total, available);
        public DiskSpaceInfo GetMasterDiskSpace() => _info;
    }

    [Fact]
    public async Task GetAllNodesAsync_LocalNode_DynamicallyUpdatesFromDiskTelemetry()
    {
        using var db = CreateDbContext();
        var telemetry = new FakeDiskTelemetryProvider(500_000_000_000L, 200_000_000_000L);
        var service = CreateService(db, telemetry);

        var localNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Master Local",
            Type = StorageNodeType.LocalDisk,
            Status = StorageNodeStatus.Online,
            TotalSpaceBytes = 100,
            AvailableSpaceBytes = 50,
            MaxCapacityBytes = 500_000_000_000L
        };
        db.StorageNodes.Add(localNode);
        await db.SaveChangesAsync();

        var nodes = await service.GetAllNodesAsync();
        var returned = Assert.Single(nodes);
        Assert.Equal(500_000_000_000L, returned.TotalSpaceBytes);
        Assert.Equal(200_000_000_000L, returned.AvailableSpaceBytes);

        var inDb = await db.StorageNodes.FindAsync(localNode.Id);
        Assert.Equal(500_000_000_000L, inDb!.TotalSpaceBytes);
        Assert.Equal(200_000_000_000L, inDb.AvailableSpaceBytes);
    }

    [Fact]
    public async Task UpdateNodeAsync_LocalNode_CapacityExceedingPhysicalDisk_ThrowsValidationException()
    {
        using var db = CreateDbContext();
        var telemetry = new FakeDiskTelemetryProvider(500_000_000_000L, 200_000_000_000L);
        var service = CreateService(db, telemetry);

        var localNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Master Local",
            Type = StorageNodeType.LocalDisk,
            Status = StorageNodeStatus.Online,
            MaxCapacityBytes = 400_000_000_000L
        };
        db.StorageNodes.Add(localNode);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.UpdateNodeAsync(localNode.Id, new UpdateStorageNodeRequest
            {
                MaxCapacityBytes = 600_000_000_000L
            }));

        Assert.Contains("не может превышать физический размер диска", ex.Message);
    }

    [Fact]
    public async Task UpdateNodeAsync_CapacityBelowActualUsedBytes_ThrowsValidationException()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var node = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Daemon-Node",
            Type = StorageNodeType.RemoteDaemon,
            Status = StorageNodeStatus.Online,
            MaxCapacityBytes = 100_000_000L,
            IsActive = true
        };
        db.StorageNodes.Add(node);

        var file = new FileItem
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Name = "used.dat",
            StorageBlobId = Guid.NewGuid(),
            SizeBytes = 50_000_000L,
            MimeType = "application/octet-stream",
            Extension = ".dat",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
        db.FileItems.Add(file);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.UpdateNodeAsync(node.Id, new UpdateStorageNodeRequest
            {
                MaxCapacityBytes = 20_000_000L
            }));

        Assert.Contains("Невозможно уменьшить емкость нод ниже фактически занятого объема файлов", ex.Message);
    }

    [Fact]
    public async Task UpdateNodeAsync_WithOversoldQuotas_SucceedsWhenFilesFit()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var node = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Daemon-Node",
            Type = StorageNodeType.RemoteDaemon,
            Status = StorageNodeStatus.Online,
            MaxCapacityBytes = 100_000_000L,
            IsActive = true
        };
        db.StorageNodes.Add(node);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "oversold_user",
            Email = "oversold@test.local",
            PasswordHash = "hash",
            Role = UserRole.User,
            AllocatedQuotaBytes = 200_000_000L,
            UsedQuotaBytes = 5_000_000L,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);

        var file = new FileItem
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Name = "small.dat",
            StorageBlobId = Guid.NewGuid(),
            SizeBytes = 5_000_000L,
            MimeType = "application/octet-stream",
            Extension = ".dat",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
        db.FileItems.Add(file);
        await db.SaveChangesAsync();

        var updated = await service.UpdateNodeAsync(node.Id, new UpdateStorageNodeRequest
        {
            MaxCapacityBytes = 50_000_000L
        });

        Assert.Equal(50_000_000L, updated.MaxCapacityBytes);
    }

    [Fact]
    public async Task GetAllNodesAsync_LocalNode_WithOldHeartbeat_IsOnlineAndZeroLag()
    {
        using var db = CreateDbContext();
        var service = CreateService(db);

        var localNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Master Local",
            Type = StorageNodeType.LocalDisk,
            Status = StorageNodeStatus.Online,
            TotalSpaceBytes = 1_000_000_000L,
            AvailableSpaceBytes = 500_000_000L,
            MaxCapacityBytes = 1_000_000_000L,
            LastHeartbeatAt = DateTime.UtcNow.AddHours(-5) // 5 hours ago
        };
        db.StorageNodes.Add(localNode);
        await db.SaveChangesAsync();

        var nodes = await service.GetAllNodesAsync();
        var nodeDto = Assert.Single(nodes);
        Assert.True(nodeDto.IsOnline);
        Assert.Equal(0, nodeDto.SecondsSinceLastHeartbeat);
    }

    [Fact]
    public async Task UpdateNodeAsync_LocalNode_WithoutDiskTelemetry_ValidatesAgainstNodeTotalSpaceBytes()
    {
        using var db = CreateDbContext();
        var service = CreateService(db); // telemetry is null

        var localNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Master Local",
            Type = StorageNodeType.LocalDisk,
            Status = StorageNodeStatus.Online,
            TotalSpaceBytes = 100_000_000L,
            AvailableSpaceBytes = 50_000_000L,
            MaxCapacityBytes = 80_000_000L
        };
        db.StorageNodes.Add(localNode);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.UpdateNodeAsync(localNode.Id, new UpdateStorageNodeRequest
            {
                MaxCapacityBytes = 150_000_000L // Exceeds 100_000_000L
            }));

        Assert.Contains("не может превышать физический размер диска", ex.Message);
    }
}
