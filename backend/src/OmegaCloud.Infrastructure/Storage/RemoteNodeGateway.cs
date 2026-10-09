using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Infrastructure.Storage;

public class RemoteNodeGateway : IRemoteNodeGateway
{
    private readonly HttpClient _httpClient;
    private readonly IHmacSigner _hmacSigner;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<RemoteNodeGateway> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public RemoteNodeGateway(
        HttpClient httpClient,
        IHmacSigner hmacSigner,
        IDateTimeProvider dateTimeProvider,
        ILogger<RemoteNodeGateway> logger)
    {
        _httpClient = httpClient;
        _hmacSigner = hmacSigner;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public Task<PreSignedUrlResult?> TryGenerateDirectDownloadUrlAsync(
        StorageNode node,
        StorageBlob blob,
        TimeSpan validFor,
        CancellationToken ct = default)
    {
        // Direct download is only available if node is active, online, not behind strict NAT, and has a public BaseUrl
        if (!node.IsActive ||
            node.Status != Domain.Enums.StorageNodeStatus.Online ||
            node.IsBehindNat ||
            string.IsNullOrWhiteSpace(node.BaseUrl) ||
            string.IsNullOrWhiteSpace(node.NodeSecretHash))
        {
            return Task.FromResult<PreSignedUrlResult?>(null);
        }

        var url = _hmacSigner.GenerateDirectDownloadUrl(node.BaseUrl, blob.Id, validFor, node.NodeSecretHash);
        var expiresAt = _dateTimeProvider.UtcNow.Add(validFor);

        return Task.FromResult<PreSignedUrlResult?>(new PreSignedUrlResult
        {
            Url = url,
            ExpiresAt = expiresAt
        });
    }

    public Task<PreSignedUrlResult?> TryGenerateDirectUploadUrlAsync(
        StorageNode node,
        UploadSession session,
        int chunkIndex,
        TimeSpan validFor,
        CancellationToken ct = default)
    {
        // Direct chunk upload is only available if node is active, online, not behind strict NAT, and has a public BaseUrl
        if (!node.IsActive ||
            node.Status != Domain.Enums.StorageNodeStatus.Online ||
            node.IsBehindNat ||
            string.IsNullOrWhiteSpace(node.BaseUrl) ||
            string.IsNullOrWhiteSpace(node.NodeSecretHash))
        {
            return Task.FromResult<PreSignedUrlResult?>(null);
        }

        var url = _hmacSigner.GenerateDirectUploadChunkUrl(node.BaseUrl, session.Id, chunkIndex, validFor, node.NodeSecretHash);
        var expiresAt = _dateTimeProvider.UtcNow.Add(validFor);

        return Task.FromResult<PreSignedUrlResult?>(new PreSignedUrlResult
        {
            Url = url,
            ExpiresAt = expiresAt
        });
    }

    public async Task<Stream> OpenReadAsync(StorageNode node, StorageBlob blob, CancellationToken ct = default)
    {
        EnsureNodeReachable(node);

        var requestUrl = $"{node.BaseUrl!.TrimEnd('/')}/api/v1/blobs/{blob.Id}";
        var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        AddAuthenticationHeaders(request, node);

        try
        {
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = response.StatusCode;
                var reason = response.ReasonPhrase;
                response.Dispose();
                request.Dispose();

                if (statusCode == System.Net.HttpStatusCode.NotFound)
                {
                    throw new NotFoundException($"Blob '{blob.Id}' was not found on remote node '{node.Id}'.");
                }
                throw new InvalidOperationException($"Remote node returned error {(int)statusCode}: {reason}");
            }

            var contentStream = await response.Content.ReadAsStreamAsync(ct);
            return new HttpResponseStream(contentStream, response, request);
        }
        catch (Exception ex) when (ex is not NotFoundException && ex is not InvalidOperationException)
        {
            request.Dispose();
            _logger.LogError(ex, "Failed to stream blob {BlobId} from remote storage node {NodeId}", blob.Id, node.Id);
            throw new InvalidOperationException($"Failed to reach remote storage node '{node.Name}': {ex.Message}", ex);
        }
    }

    public async Task<StorageBlobResult> WriteAsync(
        StorageNode node,
        Stream contentStream,
        StorageWriteOptions options,
        CancellationToken ct = default)
    {
        EnsureNodeReachable(node);

        var requestUrl = $"{node.BaseUrl!.TrimEnd('/')}/api/v1/blobs";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        AddAuthenticationHeaders(request, node);

        using var streamContent = new StreamContent(contentStream);
        if (!string.IsNullOrWhiteSpace(options.ContentType))
        {
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(options.ContentType);
        }
        request.Content = streamContent;

        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<StorageBlobResult>(_jsonOptions, ct);
            return result ?? throw new InvalidOperationException("Empty response received from remote storage node.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write stream to remote storage node {NodeId}", node.Id);
            throw new InvalidOperationException($"Failed to write stream to remote storage node '{node.Name}': {ex.Message}", ex);
        }
    }

    public async Task DeleteAsync(StorageNode node, StorageBlob blob, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(node.BaseUrl))
        {
            return;
        }

        var requestUrl = $"{node.BaseUrl.TrimEnd('/')}/api/v1/blobs/{blob.Id}";
        using var request = new HttpRequestMessage(HttpMethod.Delete, requestUrl);
        AddAuthenticationHeaders(request, node);

        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Remote node {NodeId} returned {(int)response.StatusCode} on blob delete {BlobId}",
                    node.Id, response.StatusCode, blob.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete blob {BlobId} on remote node {NodeId}", blob.Id, node.Id);
        }
    }

    public async Task<UploadChunkResult> WriteChunkAsync(
        StorageNode node,
        UploadSession session,
        int chunkIndex,
        Stream chunkStream,
        string? expectedSha256 = null,
        CancellationToken ct = default)
    {
        EnsureNodeReachable(node);

        var requestUrl = $"{node.BaseUrl!.TrimEnd('/')}/api/v1/upload/{session.Id}/chunks/{chunkIndex}";
        using var request = new HttpRequestMessage(HttpMethod.Put, requestUrl);
        AddAuthenticationHeaders(request, node);

        using var streamContent = new StreamContent(chunkStream);
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            request.Headers.Add("X-Chunk-SHA256", expectedSha256);
        }
        request.Content = streamContent;

        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<UploadChunkResult>(_jsonOptions, ct);
            return result ?? throw new InvalidOperationException("Empty response received from remote node during chunk upload.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write chunk {ChunkIndex} for session {SessionId} on node {NodeId}",
                chunkIndex, session.Id, node.Id);
            throw new InvalidOperationException($"Failed to write chunk to remote node '{node.Name}': {ex.Message}", ex);
        }
    }

    public async Task<StorageBlobResult> AssembleChunksAsync(
        StorageNode node,
        UploadSession session,
        IReadOnlyList<UploadChunk> chunks,
        CancellationToken ct = default)
    {
        EnsureNodeReachable(node);

        var requestUrl = $"{node.BaseUrl!.TrimEnd('/')}/api/v1/upload/{session.Id}/assemble";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        AddAuthenticationHeaders(request, node);
        request.Content = JsonContent.Create(new { totalChunks = session.TotalChunks, expectedSha256 = session.ExpectedSha256 });

        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<StorageBlobResult>(_jsonOptions, ct);
            return result ?? throw new InvalidOperationException("Empty response received from remote node during assemble.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assemble chunks for session {SessionId} on node {NodeId}", session.Id, node.Id);
            throw new InvalidOperationException($"Failed to assemble chunks on remote node '{node.Name}': {ex.Message}", ex);
        }
    }

    public async Task DeleteSessionTempDataAsync(StorageNode node, UploadSession session, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(node.BaseUrl))
        {
            return;
        }

        var requestUrl = $"{node.BaseUrl.TrimEnd('/')}/api/v1/upload/{session.Id}";
        using var request = new HttpRequestMessage(HttpMethod.Delete, requestUrl);
        AddAuthenticationHeaders(request, node);

        try
        {
            await _httpClient.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete session temp data {SessionId} on node {NodeId}", session.Id, node.Id);
        }
    }

    public async Task<bool> PingAsync(StorageNode node, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(node.BaseUrl))
        {
            return false;
        }

        var pingUrl = $"{node.BaseUrl.TrimEnd('/')}/api/v1/ping";
        using var request = new HttpRequestMessage(HttpMethod.Get, pingUrl);
        AddAuthenticationHeaders(request, node);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var response = await _httpClient.SendAsync(request, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<int?> MeasureLatencyMsAsync(StorageNode node, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(node.BaseUrl))
        {
            return null;
        }

        var pingUrl = $"{node.BaseUrl.TrimEnd('/')}/api/v1/ping";
        using var request = new HttpRequestMessage(HttpMethod.Get, pingUrl);
        AddAuthenticationHeaders(request, node);

        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var response = await _httpClient.SendAsync(request, cts.Token);
            sw.Stop();

            return response.IsSuccessStatusCode ? (int)sw.ElapsedMilliseconds : null;
        }
        catch
        {
            return null;
        }
    }

    private void AddAuthenticationHeaders(HttpRequestMessage request, StorageNode node)
    {
        var timestamp = new DateTimeOffset(_dateTimeProvider.UtcNow).ToUnixTimeSeconds().ToString();
        var signature = _hmacSigner.ComputeHmacSha256($"{node.Id}:{timestamp}", node.NodeSecretHash);

        request.Headers.Add("X-Node-Id", node.Id.ToString());
        request.Headers.Add("X-Node-Timestamp", timestamp);
        request.Headers.Add("X-Node-Signature", signature);
    }

    private static void EnsureNodeReachable(StorageNode node)
    {
        if (!node.IsActive)
        {
            throw new InvalidOperationException($"Storage node '{node.Name}' is deactivated.");
        }

        if (node.Status == Domain.Enums.StorageNodeStatus.Offline)
        {
            throw new InvalidOperationException($"Storage node '{node.Name}' is offline.");
        }

        if (string.IsNullOrWhiteSpace(node.BaseUrl))
        {
            throw new InvalidOperationException($"Storage node '{node.Name}' has no configured BaseUrl for communication.");
        }
    }

    private sealed class HttpResponseStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly HttpResponseMessage _response;
        private readonly HttpRequestMessage _request;

        public HttpResponseStream(Stream innerStream, HttpResponseMessage response, HttpRequestMessage request)
        {
            _innerStream = innerStream;
            _response = response;
            _request = request;
        }

        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => _innerStream.CanSeek;
        public override bool CanWrite => _innerStream.CanWrite;
        public override long Length => _innerStream.Length;
        public override long Position
        {
            get => _innerStream.Position;
            set => _innerStream.Position = value;
        }

        public override void Flush() => _innerStream.Flush();
        public override Task FlushAsync(CancellationToken ct) => _innerStream.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => _innerStream.ReadAsync(buffer, offset, count, ct);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _innerStream.ReadAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
        public override void SetLength(long value) => _innerStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => _innerStream.WriteAsync(buffer, offset, count, ct);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => _innerStream.WriteAsync(buffer, ct);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _innerStream.Dispose();
                _response.Dispose();
                _request.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _innerStream.DisposeAsync();
            _response.Dispose();
            _request.Dispose();
            await base.DisposeAsync();
        }
    }
}
