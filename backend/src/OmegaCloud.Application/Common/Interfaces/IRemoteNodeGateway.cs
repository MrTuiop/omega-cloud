using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Application.Common.Interfaces;

public interface IRemoteNodeGateway
{
    Task<PreSignedUrlResult?> TryGenerateDirectDownloadUrlAsync(StorageNode node, StorageBlob blob, TimeSpan validFor, CancellationToken ct = default);
    Task<PreSignedUrlResult?> TryGenerateDirectUploadUrlAsync(StorageNode node, UploadSession session, int chunkIndex, TimeSpan validFor, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(StorageNode node, StorageBlob blob, CancellationToken ct = default);
    Task<StorageBlobResult> WriteAsync(StorageNode node, Stream contentStream, StorageWriteOptions options, CancellationToken ct = default);
    Task DeleteAsync(StorageNode node, StorageBlob blob, CancellationToken ct = default);
    Task<UploadChunkResult> WriteChunkAsync(StorageNode node, UploadSession session, int chunkIndex, Stream chunkStream, string? expectedSha256 = null, CancellationToken ct = default);
    Task<StorageBlobResult> AssembleChunksAsync(StorageNode node, UploadSession session, IReadOnlyList<UploadChunk> chunks, CancellationToken ct = default);
    Task DeleteSessionTempDataAsync(StorageNode node, UploadSession session, CancellationToken ct = default);
    Task<bool> PingAsync(StorageNode node, CancellationToken ct = default);
    Task<int?> MeasureLatencyMsAsync(StorageNode node, CancellationToken ct = default);
}
