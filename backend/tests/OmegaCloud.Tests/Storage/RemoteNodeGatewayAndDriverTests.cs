using Microsoft.Extensions.Logging.Abstractions;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;
using OmegaCloud.Infrastructure.Security;
using OmegaCloud.Infrastructure.Services;
using OmegaCloud.Infrastructure.Storage;

namespace OmegaCloud.Tests.Storage;

public class RemoteNodeGatewayAndDriverTests
{
    private readonly HmacSigner _hmacSigner;
    private readonly DateTimeProvider _dateTimeProvider;
    private readonly RemoteNodeGateway _gateway;
    private readonly LocalDiskStorageDriver _localDiskDriver;
    private readonly StorageDriverResolver _resolver;

    public RemoteNodeGatewayAndDriverTests()
    {
        _dateTimeProvider = new DateTimeProvider();
        _hmacSigner = new HmacSigner(_dateTimeProvider);
        _gateway = new RemoteNodeGateway(
            new HttpClient(),
            _hmacSigner,
            _dateTimeProvider,
            NullLogger<RemoteNodeGateway>.Instance);

        var tempDir = Path.Combine(Path.GetTempPath(), "StorageTest_" + Guid.NewGuid().ToString("N"));
        _localDiskDriver = new LocalDiskStorageDriver(tempDir, NullLogger<LocalDiskStorageDriver>.Instance);
        _resolver = new StorageDriverResolver(_localDiskDriver, _gateway);
    }

    [Fact]
    public async Task TryGenerateDirectDownloadUrl_DirectNode_ReturnsPreSignedUrl()
    {
        var node = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Direct NAS",
            Type = StorageNodeType.RemoteDaemon,
            BaseUrl = "https://nas.example.com:5001",
            IsBehindNat = false,
            NodeSecretHash = "secret_node_key_123",
            Status = StorageNodeStatus.Online
        };

        var blob = new StorageBlob
        {
            Id = Guid.NewGuid(),
            StorageNodeId = node.Id,
            StorageNode = node,
            StorageRelativePath = "blobs/12/34/dummy.blob"
        };

        var result = await _gateway.TryGenerateDirectDownloadUrlAsync(node, blob, TimeSpan.FromMinutes(15));

        Assert.NotNull(result);
        Assert.StartsWith("https://nas.example.com:5001/api/v1/download/", result.Url);
        Assert.Contains("expires=", result.Url);
        Assert.Contains("signature=", result.Url);
    }

    [Fact]
    public async Task TryGenerateDirectDownloadUrl_BehindNatOrNoBaseUrl_ReturnsNull()
    {
        var natNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "NAT NAS",
            Type = StorageNodeType.RemoteDaemon,
            BaseUrl = "https://nas.example.com:5001",
            IsBehindNat = true,
            NodeSecretHash = "secret_node_key_123"
        };

        var noUrlNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "No URL NAS",
            Type = StorageNodeType.RemoteDaemon,
            BaseUrl = null,
            IsBehindNat = false,
            NodeSecretHash = "secret_node_key_123"
        };

        var blob = new StorageBlob { Id = Guid.NewGuid() };

        var natResult = await _gateway.TryGenerateDirectDownloadUrlAsync(natNode, blob, TimeSpan.FromMinutes(15));
        var noUrlResult = await _gateway.TryGenerateDirectDownloadUrlAsync(noUrlNode, blob, TimeSpan.FromMinutes(15));

        Assert.Null(natResult);
        Assert.Null(noUrlResult);
    }

    [Fact]
    public async Task TryGenerateDirectUrls_OfflineOrDeactivatedNode_ReturnsNull()
    {
        var offlineNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Offline NAS",
            Type = StorageNodeType.RemoteDaemon,
            BaseUrl = "https://nas.example.com:5001",
            IsBehindNat = false,
            NodeSecretHash = "secret_node_key_123",
            Status = StorageNodeStatus.Offline,
            IsActive = true
        };

        var inactiveNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Inactive NAS",
            Type = StorageNodeType.RemoteDaemon,
            BaseUrl = "https://nas.example.com:5001",
            IsBehindNat = false,
            NodeSecretHash = "secret_node_key_123",
            Status = StorageNodeStatus.Online,
            IsActive = false
        };

        var blob = new StorageBlob { Id = Guid.NewGuid() };
        var session = new UploadSession { Id = Guid.NewGuid(), StorageNode = offlineNode };

        Assert.Null(await _gateway.TryGenerateDirectDownloadUrlAsync(offlineNode, blob, TimeSpan.FromMinutes(15)));
        Assert.Null(await _gateway.TryGenerateDirectDownloadUrlAsync(inactiveNode, blob, TimeSpan.FromMinutes(15)));
        Assert.Null(await _gateway.TryGenerateDirectUploadUrlAsync(offlineNode, session, 0, TimeSpan.FromMinutes(15)));
        Assert.Null(await _gateway.TryGenerateDirectUploadUrlAsync(inactiveNode, session, 0, TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public async Task TryGenerateDirectUploadUrl_DirectNode_ReturnsPreSignedUrl()
    {
        var node = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Direct NAS",
            Type = StorageNodeType.RemoteDaemon,
            BaseUrl = "https://nas.example.com:5001",
            IsBehindNat = false,
            NodeSecretHash = "secret_node_key_123",
            Status = StorageNodeStatus.Online
        };

        var session = new UploadSession
        {
            Id = Guid.NewGuid(),
            StorageNodeId = node.Id,
            StorageNode = node
        };

        var result = await _gateway.TryGenerateDirectUploadUrlAsync(node, session, 0, TimeSpan.FromMinutes(15));

        Assert.NotNull(result);
        Assert.StartsWith($"https://nas.example.com:5001/api/v1/upload/{session.Id}/chunks/0?", result.Url);
        Assert.Contains("signature=", result.Url);
    }

    [Fact]
    public async Task RemoteNodeStorageDriver_DelegatesDirectUrlsCorrectly()
    {
        var node = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Direct NAS",
            Type = StorageNodeType.RemoteDaemon,
            BaseUrl = "https://nas.example.com:5001",
            IsBehindNat = false,
            NodeSecretHash = "secret_node_key_123",
            Status = StorageNodeStatus.Online
        };

        var driver = new RemoteNodeStorageDriver(node, _gateway);
        Assert.Equal("RemoteDaemon", driver.DriverType);

        var blob = new StorageBlob { Id = Guid.NewGuid() };
        var directUrl = await driver.TryGenerateDirectDownloadUrlAsync(blob, TimeSpan.FromMinutes(10));

        Assert.NotNull(directUrl);
        Assert.StartsWith("https://nas.example.com:5001/api/v1/download/", directUrl.Url);
    }

    [Fact]
    public void StorageDriverResolver_ResolvesCorrectDrivers()
    {
        var localDriver = _resolver.GetDriver(StorageNodeType.LocalDisk);
        Assert.Equal("LocalDisk", localDriver.DriverType);

        var remoteDriver = _resolver.GetDriver(StorageNodeType.RemoteDaemon);
        Assert.Equal("RemoteDaemon", remoteDriver.DriverType);

        var remoteNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Type = StorageNodeType.RemoteDaemon,
            BaseUrl = "https://remote.test:5000"
        };
        var nodeBoundDriver = _resolver.GetDriverForNode(remoteNode);
        Assert.Equal("RemoteDaemon", nodeBoundDriver.DriverType);
    }
}
