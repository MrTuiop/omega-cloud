using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;
using OmegaCloud.Infrastructure.Security;
using OmegaCloud.Infrastructure.Storage;

namespace Omega.StorageNode.Tests;

public class MasterStorageNodeInteropTests : IClassFixture<StorageNodeIntegrationTests.CustomNodeWebApplicationFactory>, IDisposable
{
    private readonly StorageNodeIntegrationTests.CustomNodeWebApplicationFactory _factory;
    private readonly HttpClient _nodeClient;
    private readonly HmacSigner _hmacSigner;
    private readonly RemoteNodeGateway _gateway;
    private readonly RemoteNodeStorageDriver _driver;
    private readonly OmegaCloud.Domain.Entities.StorageNode _boundNode;

    private class InteropDateTimeProvider : OmegaCloud.Application.Common.Interfaces.IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    public MasterStorageNodeInteropTests(StorageNodeIntegrationTests.CustomNodeWebApplicationFactory factory)
    {
        _factory = factory;
        _nodeClient = factory.CreateClient();
        _nodeClient.BaseAddress = new Uri("http://localhost");

        var dtProvider = new InteropDateTimeProvider();
        _hmacSigner = new HmacSigner(dtProvider);
        _gateway = new RemoteNodeGateway(_nodeClient, _hmacSigner, dtProvider, NullLogger<RemoteNodeGateway>.Instance);

        _boundNode = new OmegaCloud.Domain.Entities.StorageNode
        {
            Id = _factory.TestNodeId,
            Name = "Integration-Test-Node",
            Type = StorageNodeType.RemoteDaemon,
            BaseUrl = "http://localhost",
            IsBehindNat = false,
            NodeSecretHash = _factory.TestNodeSecret,
            Status = StorageNodeStatus.Online,
            IsActive = true
        };

        _driver = new RemoteNodeStorageDriver(_boundNode, _gateway);
    }

    public void Dispose()
    {
        _nodeClient.Dispose();
    }

    [Fact]
    public async Task MasterGateway_PingAndMeasureLatency_Succeeds()
    {
        var pingSuccess = await _gateway.PingAsync(_boundNode);
        Assert.True(pingSuccess);

        var latency = await _gateway.MeasureLatencyMsAsync(_boundNode);
        Assert.NotNull(latency);
        Assert.True(latency.Value >= 0);
    }

    [Fact]
    public async Task MasterGateway_WriteDirectBlobAndOpenRead_ReturnsExactBytes()
    {
        var data = Encoding.UTF8.GetBytes("Master gateway direct write blob payload - 1234567890");
        var expectedSha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        using var contentStream = new MemoryStream(data);
        var writeOptions = new StorageWriteOptions
        {
            ExpectedSha256 = expectedSha,
            ExpectedSize = data.Length,
            ContentType = "text/plain"
        };

        var writeResult = await _gateway.WriteAsync(_boundNode, contentStream, writeOptions);
        Assert.NotNull(writeResult);
        Assert.Equal(expectedSha, writeResult.Sha256Hash);
        Assert.Equal(data.Length, writeResult.SizeBytes);

        var blob = new StorageBlob
        {
            Id = writeResult.BlobId,
            StorageNodeId = _boundNode.Id,
            StorageNode = _boundNode,
            Sha256Hash = writeResult.Sha256Hash,
            SizeBytes = writeResult.SizeBytes,
            StorageRelativePath = writeResult.StorageRelativePath
        };

        await using var readStream = await _gateway.OpenReadAsync(_boundNode, blob);
        using var ms = new MemoryStream();
        await readStream.CopyToAsync(ms);

        Assert.Equal(data, ms.ToArray());

        // Delete blob via gateway
        await _gateway.DeleteAsync(_boundNode, blob);

        // Subsequent read throws NotFoundException
        await Assert.ThrowsAsync<OmegaCloud.Application.Common.Exceptions.NotFoundException>(async () =>
        {
            await _gateway.OpenReadAsync(_boundNode, blob);
        });
    }

    [Fact]
    public async Task MasterGateway_WriteChunksAndAssemble_SucceedsWithDirectPreSignedDownload()
    {
        var sessionId = Guid.NewGuid();
        var part0Data = Encoding.UTF8.GetBytes("Chunk Part 0 content. ");
        var part1Data = Encoding.UTF8.GetBytes("Chunk Part 1 content.");
        var fullData = new List<byte>();
        fullData.AddRange(part0Data);
        fullData.AddRange(part1Data);
        var expectedFullSha = Convert.ToHexString(SHA256.HashData(fullData.ToArray())).ToLowerInvariant();

        var session = new UploadSession
        {
            Id = sessionId,
            StorageNodeId = _boundNode.Id,
            StorageNode = _boundNode,
            TotalChunks = 2,
            TotalSizeBytes = fullData.Count,
            ExpectedSha256 = expectedFullSha
        };

        // Write Chunk 0 via gateway
        using (var s0 = new MemoryStream(part0Data))
        {
            var res0 = await _gateway.WriteChunkAsync(_boundNode, session, 0, s0);
            Assert.Equal(0, res0.ChunkIndex);
            Assert.Equal(part0Data.Length, res0.SizeBytes);
        }

        // Write Chunk 1 via gateway
        using (var s1 = new MemoryStream(part1Data))
        {
            var res1 = await _gateway.WriteChunkAsync(_boundNode, session, 1, s1);
            Assert.Equal(1, res1.ChunkIndex);
            Assert.Equal(part1Data.Length, res1.SizeBytes);
        }

        // Assemble chunks via gateway
        var chunksList = new List<UploadChunk>
        {
            new() { ChunkIndex = 0, SizeBytes = part0Data.Length },
            new() { ChunkIndex = 1, SizeBytes = part1Data.Length }
        };

        var assembleResult = await _gateway.AssembleChunksAsync(_boundNode, session, chunksList);
        Assert.NotNull(assembleResult);
        Assert.Equal(expectedFullSha, assembleResult.Sha256Hash);
        Assert.Equal(fullData.Count, assembleResult.SizeBytes);

        // Generate Pre-signed Direct Download URL via Gateway
        var blob = new StorageBlob
        {
            Id = assembleResult.BlobId,
            StorageNodeId = _boundNode.Id,
            StorageNode = _boundNode,
            Sha256Hash = assembleResult.Sha256Hash,
            SizeBytes = assembleResult.SizeBytes
        };

        var preSignedUrl = await _gateway.TryGenerateDirectDownloadUrlAsync(_boundNode, blob, TimeSpan.FromMinutes(15));
        Assert.NotNull(preSignedUrl);

        // Fetch direct download using pre-signed URL with simulated browser client
        var directResp = await _nodeClient.GetAsync(preSignedUrl.Url);
        Assert.Equal(HttpStatusCode.OK, directResp.StatusCode);
        var directBytes = await directResp.Content.ReadAsByteArrayAsync();
        Assert.Equal(fullData.ToArray(), directBytes);

        // Clean session temp data via gateway
        await _gateway.DeleteSessionTempDataAsync(_boundNode, session);

        // Clean blob
        await _gateway.DeleteAsync(_boundNode, blob);
    }

    [Fact]
    public async Task MasterHeartbeatDeserialization_StringEnum_IsAccepted()
    {
        // Test that Master's JSON deserialization accepts "Online" string enum
        var jsonWithEnum = """
        {
            "nodeId": "11111111-2222-3333-4444-555555555555",
            "totalSpaceBytes": 1000000000,
            "availableSpaceBytes": 800000000,
            "cpuLoad": 22.5,
            "diskStatus": "Healthy",
            "baseUrl": "https://nas.example.local:5001",
            "status": "Online"
        }
        """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var deserialized = JsonSerializer.Deserialize<OmegaCloud.Application.DTOs.Nodes.NodeHeartbeatRequest>(jsonWithEnum, options);
        Assert.NotNull(deserialized);
        Assert.Equal(StorageNodeStatus.Online, deserialized.Status);
    }
}
