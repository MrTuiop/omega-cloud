using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Infrastructure.Storage;

public class RemoteNodeStorageDriver : IStorageDriver
{
    private readonly StorageNode? _boundNode;
    private readonly IRemoteNodeGateway _gateway;

    public string DriverType => "RemoteDaemon";

    public RemoteNodeStorageDriver(IRemoteNodeGateway gateway)
    {
        _gateway = gateway;
        _boundNode = null;
    }

    public RemoteNodeStorageDriver(StorageNode boundNode, IRemoteNodeGateway gateway)
    {
        _boundNode = boundNode;
        _gateway = gateway;
    }

    public Task<Stream> OpenReadAsync(StorageBlob blob, CancellationToken ct = default)
    {
        var node = ResolveNode(blob: blob);
        return _gateway.OpenReadAsync(node, blob, ct);
    }

    public Task<StorageBlobResult> WriteAsync(Stream contentStream, StorageWriteOptions options, CancellationToken ct = default)
    {
        var node = ResolveNode();
        return _gateway.WriteAsync(node, contentStream, options, ct);
    }

    public Task DeleteAsync(StorageBlob blob, CancellationToken ct = default)
    {
        var node = ResolveNode(blob: blob);
        return _gateway.DeleteAsync(node, blob, ct);
    }

    public Task<PreSignedUrlResult?> TryGenerateDirectDownloadUrlAsync(StorageBlob blob, TimeSpan validFor, CancellationToken ct = default)
    {
        var node = ResolveNode(blob: blob);
        return _gateway.TryGenerateDirectDownloadUrlAsync(node, blob, validFor, ct);
    }

    public Task<PreSignedUrlResult?> TryGenerateDirectUploadUrlAsync(UploadSession session, int chunkIndex, TimeSpan validFor, CancellationToken ct = default)
    {
        var node = ResolveNode(session: session);
        return _gateway.TryGenerateDirectUploadUrlAsync(node, session, chunkIndex, validFor, ct);
    }

    public Task<bool> PingAsync(CancellationToken ct = default)
    {
        if (_boundNode != null)
        {
            return _gateway.PingAsync(_boundNode, ct);
        }
        return Task.FromResult(true);
    }

    public Task<UploadChunkResult> WriteChunkAsync(
        UploadSession session,
        int chunkIndex,
        Stream chunkStream,
        string? expectedSha256 = null,
        CancellationToken ct = default)
    {
        var node = ResolveNode(session: session);
        return _gateway.WriteChunkAsync(node, session, chunkIndex, chunkStream, expectedSha256, ct);
    }

    public Task<StorageBlobResult> AssembleChunksAsync(UploadSession session, IReadOnlyList<UploadChunk> chunks, CancellationToken ct = default)
    {
        var node = ResolveNode(session: session);
        return _gateway.AssembleChunksAsync(node, session, chunks, ct);
    }

    public Task DeleteSessionTempDataAsync(UploadSession session, CancellationToken ct = default)
    {
        var node = ResolveNode(session: session);
        return _gateway.DeleteSessionTempDataAsync(node, session, ct);
    }

    private StorageNode ResolveNode(StorageBlob? blob = null, UploadSession? session = null)
    {
        if (_boundNode != null)
        {
            return _boundNode;
        }

        if (blob?.StorageNode != null)
        {
            return blob.StorageNode;
        }

        if (session?.StorageNode != null)
        {
            return session.StorageNode;
        }

        throw new InvalidOperationException("No storage node context is available for the remote daemon driver operation.");
    }
}
