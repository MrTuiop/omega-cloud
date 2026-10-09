using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Omega.StorageNode.Configuration;
using Omega.StorageNode.Models;
using Omega.StorageNode.Services;
using Omega.StorageNode.Workers;

namespace Omega.StorageNode.Tests;

public class StorageNodeIntegrationTests : IClassFixture<StorageNodeIntegrationTests.CustomNodeWebApplicationFactory>, IDisposable
{
    private readonly CustomNodeWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public StorageNodeIntegrationTests(CustomNodeWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    [Fact]
    public async Task HealthEndpoint_Returns200AndNodeStatus()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", json);
    }

    [Fact]
    public async Task PingEndpoint_ValidMasterHmacHeaders_Returns200()
    {
        var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = ComputeHmac($"{_factory.TestNodeId}:{unixTimestamp}", _factory.TestNodeSecret);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ping");
        request.Headers.Add("X-Node-Id", _factory.TestNodeId.ToString());
        request.Headers.Add("X-Node-Timestamp", unixTimestamp.ToString());
        request.Headers.Add("X-Node-Signature", signature);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ping = await response.Content.ReadFromJsonAsync<PingResponse>(_jsonOptions);
        Assert.NotNull(ping);
        Assert.Equal("Healthy", ping.Status);
        Assert.Equal(_factory.TestNodeId, ping.NodeId);
    }

    [Fact]
    public async Task PingEndpoint_InvalidSignature_Returns401()
    {
        var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ping");
        request.Headers.Add("X-Node-Id", _factory.TestNodeId.ToString());
        request.Headers.Add("X-Node-Timestamp", unixTimestamp.ToString());
        request.Headers.Add("X-Node-Signature", "invalid_signature");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DirectUploadChunk_ValidPreSignedParams_Returns200()
    {
        var sessionId = Guid.NewGuid();
        var chunkIndex = 0;
        var expires = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
        var signature = ComputeHmac($"upload:{sessionId}:{chunkIndex}:{expires}", _factory.TestNodeSecret);

        var url = $"/api/v1/upload/{sessionId}/chunks/{chunkIndex}?expires={expires}&signature={signature}";
        var chunkBytes = Encoding.UTF8.GetBytes("Chunk zero content from direct browser upload");

        using var content = new ByteArrayContent(chunkBytes);
        var response = await _client.PutAsync(url, content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<UploadChunkResult>(_jsonOptions);
        Assert.NotNull(result);
        Assert.Equal(chunkIndex, result.ChunkIndex);
        Assert.Equal(chunkBytes.Length, result.SizeBytes);
    }

    [Fact]
    public async Task DirectUploadChunk_ExpiredSignature_Returns401()
    {
        var sessionId = Guid.NewGuid();
        var chunkIndex = 0;
        var expires = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
        var signature = ComputeHmac($"upload:{sessionId}:{chunkIndex}:{expires}", _factory.TestNodeSecret);

        var url = $"/api/v1/upload/{sessionId}/chunks/{chunkIndex}?expires={expires}&signature={signature}";
        var chunkBytes = Encoding.UTF8.GetBytes("Expired chunk content");

        using var content = new ByteArrayContent(chunkBytes);
        var response = await _client.PutAsync(url, content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EndToEndUploadAssembleAndDirectDownloadFlow()
    {
        var sessionId = Guid.NewGuid();

        // 1. Upload chunk 0 via direct pre-signed URL
        var part0 = Encoding.UTF8.GetBytes("Part Zero Data. ");
        var expires0 = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
        var sig0 = ComputeHmac($"upload:{sessionId}:0:{expires0}", _factory.TestNodeSecret);

        using (var c0 = new ByteArrayContent(part0))
        {
            var r0 = await _client.PutAsync($"/api/v1/upload/{sessionId}/chunks/0?expires={expires0}&signature={sig0}", c0);
            Assert.Equal(HttpStatusCode.OK, r0.StatusCode);
        }

        // 2. Upload chunk 1 via Master proxy headers
        var part1 = Encoding.UTF8.GetBytes("Part One Data.");
        var time1 = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig1 = ComputeHmac($"{_factory.TestNodeId}:{time1}", _factory.TestNodeSecret);

        using (var c1 = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/upload/{sessionId}/chunks/1"))
        {
            c1.Headers.Add("X-Node-Id", _factory.TestNodeId.ToString());
            c1.Headers.Add("X-Node-Timestamp", time1.ToString());
            c1.Headers.Add("X-Node-Signature", sig1);
            c1.Content = new ByteArrayContent(part1);

            var r1 = await _client.SendAsync(c1);
            Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        }

        // 3. Assemble chunks via Master call
        var fullBytes = new List<byte>();
        fullBytes.AddRange(part0);
        fullBytes.AddRange(part1);
        var expectedFullSha256 = Convert.ToHexString(SHA256.HashData(fullBytes.ToArray())).ToLowerInvariant();

        var assembleTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var assembleSig = ComputeHmac($"{_factory.TestNodeId}:{assembleTime}", _factory.TestNodeSecret);

        var assembleRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/upload/{sessionId}/assemble")
        {
            Content = JsonContent.Create(new AssembleChunksRequest
            {
                TotalChunks = 2,
                ExpectedSha256 = expectedFullSha256
            })
        };
        assembleRequest.Headers.Add("X-Node-Id", _factory.TestNodeId.ToString());
        assembleRequest.Headers.Add("X-Node-Timestamp", assembleTime.ToString());
        assembleRequest.Headers.Add("X-Node-Signature", assembleSig);

        var assembleResponse = await _client.SendAsync(assembleRequest);
        Assert.Equal(HttpStatusCode.OK, assembleResponse.StatusCode);

        var blobResult = await assembleResponse.Content.ReadFromJsonAsync<StorageBlobResult>(_jsonOptions);
        Assert.NotNull(blobResult);
        Assert.Equal(expectedFullSha256, blobResult.Sha256Hash);
        Assert.Equal(fullBytes.Count, blobResult.SizeBytes);

        // 4. Direct Browser Download via pre-signed URL
        var downloadExpires = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
        var downloadSig = ComputeHmac($"download:{blobResult.BlobId}:{downloadExpires}", _factory.TestNodeSecret);

        var downloadUrl = $"/api/v1/download/{blobResult.BlobId}?expires={downloadExpires}&signature={downloadSig}";
        var downloadResponse = await _client.GetAsync(downloadUrl);

        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        var downloadedBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(fullBytes.ToArray(), downloadedBytes);

        // 5. Proxy Read from Master
        var proxyTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var proxySig = ComputeHmac($"{_factory.TestNodeId}:{proxyTime}", _factory.TestNodeSecret);

        var proxyRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/blobs/{blobResult.BlobId}");
        proxyRequest.Headers.Add("X-Node-Id", _factory.TestNodeId.ToString());
        proxyRequest.Headers.Add("X-Node-Timestamp", proxyTime.ToString());
        proxyRequest.Headers.Add("X-Node-Signature", proxySig);

        var proxyResponse = await _client.SendAsync(proxyRequest);
        Assert.Equal(HttpStatusCode.OK, proxyResponse.StatusCode);
        var proxyBytes = await proxyResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(fullBytes.ToArray(), proxyBytes);

        // 6. Delete blob via Master call
        var delTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var delSig = ComputeHmac($"{_factory.TestNodeId}:{delTime}", _factory.TestNodeSecret);

        var delRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/blobs/{blobResult.BlobId}");
        delRequest.Headers.Add("X-Node-Id", _factory.TestNodeId.ToString());
        delRequest.Headers.Add("X-Node-Timestamp", delTime.ToString());
        delRequest.Headers.Add("X-Node-Signature", delSig);

        var delResponse = await _client.SendAsync(delRequest);
        Assert.Equal(HttpStatusCode.NoContent, delResponse.StatusCode);

        // 7. Verify blob is gone (404)
        var verifyRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/blobs/{blobResult.BlobId}");
        verifyRequest.Headers.Add("X-Node-Id", _factory.TestNodeId.ToString());
        verifyRequest.Headers.Add("X-Node-Timestamp", delTime.ToString());
        verifyRequest.Headers.Add("X-Node-Signature", delSig);

        var verifyResponse = await _client.SendAsync(verifyRequest);
        Assert.Equal(HttpStatusCode.NotFound, verifyResponse.StatusCode);
    }

    [Fact]
    public async Task DirectBlobWrite_FromMaster_Returns201AndCanBeRead()
    {
        var blobId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("Direct blob write stream test");
        var sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeHmac($"{_factory.TestNodeId}:{time}", _factory.TestNodeSecret);

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/blobs");
        postRequest.Headers.Add("X-Node-Id", _factory.TestNodeId.ToString());
        postRequest.Headers.Add("X-Node-Timestamp", time.ToString());
        postRequest.Headers.Add("X-Node-Signature", sig);
        postRequest.Headers.Add("X-Blob-Id", blobId.ToString());
        postRequest.Headers.Add("X-Expected-SHA256", sha256);
        postRequest.Content = new ByteArrayContent(content);

        var postResponse = await _client.SendAsync(postRequest);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        var blobResult = await postResponse.Content.ReadFromJsonAsync<StorageBlobResult>(_jsonOptions);
        Assert.NotNull(blobResult);
        Assert.Equal(blobId, blobResult.BlobId);
        Assert.Equal(sha256, blobResult.Sha256Hash);

        // Read through proxy
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/blobs/{blobId}");
        getRequest.Headers.Add("X-Node-Id", _factory.TestNodeId.ToString());
        getRequest.Headers.Add("X-Node-Timestamp", time.ToString());
        getRequest.Headers.Add("X-Node-Signature", sig);

        var getResponse = await _client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var readContent = await getResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(content, readContent);
    }

    [Fact]
    public async Task DeleteUploadSessionTemp_Returns204()
    {
        var sessionId = Guid.NewGuid();
        var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeHmac($"{_factory.TestNodeId}:{time}", _factory.TestNodeSecret);

        var delRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/upload/{sessionId}");
        delRequest.Headers.Add("X-Node-Id", _factory.TestNodeId.ToString());
        delRequest.Headers.Add("X-Node-Timestamp", time.ToString());
        delRequest.Headers.Add("X-Node-Signature", sig);

        var delResponse = await _client.SendAsync(delRequest);
        Assert.Equal(HttpStatusCode.NoContent, delResponse.StatusCode);
    }

    [Fact]
    public async Task DirectUploadChunk_MultipartFormData_Returns200AndValidChunk()
    {
        var sessionId = Guid.NewGuid();
        var chunkIndex = 0;
        var expires = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
        var signature = ComputeHmac($"upload:{sessionId}:{chunkIndex}:{expires}", _factory.TestNodeSecret);

        var url = $"/api/v1/upload/{sessionId}/chunks/{chunkIndex}?expires={expires}&signature={signature}";
        var chunkBytes = Encoding.UTF8.GetBytes("Pure clean binary chunk data without multipart headers");
        var chunkSha256 = Convert.ToHexString(SHA256.HashData(chunkBytes)).ToLowerInvariant();

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(chunkBytes), "chunk", "blob.part");
        form.Add(new StringContent(chunkSha256), "sha256");

        var response = await _client.PutAsync(url, form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<UploadChunkResult>(_jsonOptions);
        Assert.NotNull(result);
        Assert.Equal(chunkIndex, result.ChunkIndex);
        Assert.Equal(chunkBytes.Length, result.SizeBytes);
        Assert.Equal(chunkSha256, result.Sha256Hash);

        // Verify assembled content matches pure bytes
        var assembleTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var assembleSig = ComputeHmac($"{_factory.TestNodeId}:{assembleTime}", _factory.TestNodeSecret);
        var assembleReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/upload/{sessionId}/assemble")
        {
            Content = JsonContent.Create(new AssembleChunksRequest
            {
                TotalChunks = 1,
                ExpectedSha256 = chunkSha256
            })
        };
        assembleReq.Headers.Add("X-Node-Id", _factory.TestNodeId.ToString());
        assembleReq.Headers.Add("X-Node-Timestamp", assembleTime.ToString());
        assembleReq.Headers.Add("X-Node-Signature", assembleSig);

        var assembleResp = await _client.SendAsync(assembleReq);
        Assert.Equal(HttpStatusCode.OK, assembleResp.StatusCode);

        var blob = await assembleResp.Content.ReadFromJsonAsync<StorageBlobResult>(_jsonOptions);
        Assert.NotNull(blob);
        Assert.Equal(chunkBytes.Length, blob.SizeBytes);

        // Read through proxy and confirm exact byte equality
        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/blobs/{blob.BlobId}");
        getReq.Headers.Add("X-Node-Id", _factory.TestNodeId.ToString());
        getReq.Headers.Add("X-Node-Timestamp", assembleTime.ToString());
        getReq.Headers.Add("X-Node-Signature", assembleSig);

        var getResp = await _client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var readBytes = await getResp.Content.ReadAsByteArrayAsync();
        Assert.Equal(chunkBytes, readBytes);
    }

    [Fact]
    public async Task DirectDownload_WithFilenameAndMimeTypeDeduction_SetsCorrectHeaders()
    {
        // 1. Direct write a PDF file
        var blobId = Guid.NewGuid();
        var pdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4 test document content");
        var sha = Convert.ToHexString(SHA256.HashData(pdfBytes)).ToLowerInvariant();

        var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeHmac($"{_factory.TestNodeId}:{time}", _factory.TestNodeSecret);

        var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/blobs");
        postReq.Headers.Add("X-Node-Id", _factory.TestNodeId.ToString());
        postReq.Headers.Add("X-Node-Timestamp", time.ToString());
        postReq.Headers.Add("X-Node-Signature", sig);
        postReq.Headers.Add("X-Blob-Id", blobId.ToString());
        postReq.Content = new ByteArrayContent(pdfBytes);

        var postResp = await _client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Created, postResp.StatusCode);

        // 2. Direct download with filename=annual-report.pdf
        var expires = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
        var downloadSig = ComputeHmac($"download:{blobId}:{expires}", _factory.TestNodeSecret);

        var downloadUrl = $"/api/v1/download/{blobId}?expires={expires}&signature={downloadSig}&filename=annual-report.pdf";
        var downloadResp = await _client.GetAsync(downloadUrl);

        Assert.Equal(HttpStatusCode.OK, downloadResp.StatusCode);
        Assert.Equal("application/pdf", downloadResp.Content.Headers.ContentType?.MediaType);
        Assert.Equal("annual-report.pdf", downloadResp.Content.Headers.ContentDisposition?.FileName);

        var downloadedBytes = await downloadResp.Content.ReadAsByteArrayAsync();
        Assert.Equal(pdfBytes, downloadedBytes);
    }

    [Fact]
    public async Task ConcurrentChunkUploads_SameIndex_NoFileLockContention()
    {
        var sessionId = Guid.NewGuid();
        var chunkIndex = 0;
        var expires = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
        var signature = ComputeHmac($"upload:{sessionId}:{chunkIndex}:{expires}", _factory.TestNodeSecret);
        var url = $"/api/v1/upload/{sessionId}/chunks/{chunkIndex}?expires={expires}&signature={signature}";

        var data = Encoding.UTF8.GetBytes("Concurrent chunk upload data");

        // Fire 5 concurrent uploads to the exact same chunk
        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            using var content = new ByteArrayContent(data);
            return await _client.PutAsync(url, content);
        }).ToArray();

        var responses = await Task.WhenAll(tasks);
        foreach (var r in responses)
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
    }

    [Fact]
    public async Task ConcurrentReadWhileDelete_ResilientOnWindows()
    {
        var blobId = Guid.NewGuid();
        var content = new byte[200_000];
        Random.Shared.NextBytes(content);

        var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeHmac($"{_factory.TestNodeId}:{time}", _factory.TestNodeSecret);

        var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/blobs");
        postReq.Headers.Add("X-Node-Id", _factory.TestNodeId.ToString());
        postReq.Headers.Add("X-Node-Timestamp", time.ToString());
        postReq.Headers.Add("X-Node-Signature", sig);
        postReq.Headers.Add("X-Blob-Id", blobId.ToString());
        postReq.Content = new ByteArrayContent(content);

        var postResp = await _client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Created, postResp.StatusCode);

        // Open read stream
        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/blobs/{blobId}");
        getReq.Headers.Add("X-Node-Id", _factory.TestNodeId.ToString());
        getReq.Headers.Add("X-Node-Timestamp", time.ToString());
        getReq.Headers.Add("X-Node-Signature", sig);

        var getResp = await _client.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var readStream = await getResp.Content.ReadAsStreamAsync();

        // While stream is open, issue delete request
        var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/blobs/{blobId}");
        delReq.Headers.Add("X-Node-Id", _factory.TestNodeId.ToString());
        delReq.Headers.Add("X-Node-Timestamp", time.ToString());
        delReq.Headers.Add("X-Node-Signature", sig);

        var delResp = await _client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        // Finish reading from the open stream without crashing
        using var ms = new MemoryStream();
        await readStream.CopyToAsync(ms);
        Assert.Equal(content.Length, ms.Length);
        readStream.Dispose();
        getResp.Dispose();
    }

    private static string ComputeHmac(string data, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        using var hmac = new HMACSHA256(keyBytes);
        return Convert.ToHexString(hmac.ComputeHash(dataBytes)).ToLowerInvariant();
    }

    public class CustomNodeWebApplicationFactory : WebApplicationFactory<Program>
    {
        public Guid TestNodeId { get; } = Guid.NewGuid();
        public string TestNodeSecret { get; } = "integration_test_node_secret_hex_12345678";
        public string TestStorageDir { get; }

        public CustomNodeWebApplicationFactory()
        {
            TestStorageDir = Path.Combine(Path.GetTempPath(), "StorageNodeIntegrationTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TestStorageDir);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Remove background hosted services to prevent network calls to non-existent master during testing
                var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
                foreach (var hs in hostedServices)
                {
                    services.Remove(hs);
                }

                // Override configuration with test values
                services.Configure<StorageNodeOptions>(options =>
                {
                    options.StorageRootPath = TestStorageDir;
                    options.NodeId = TestNodeId;
                    options.NodeSecret = TestNodeSecret;
                    options.NodeName = "Integration-Test-Node";
                    options.BaseUrl = "https://integration-test-nas.local:5001";
                    options.IsBehindNat = false;
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (Directory.Exists(TestStorageDir))
            {
                try { Directory.Delete(TestStorageDir, recursive: true); } catch { }
            }
        }
    }
}
