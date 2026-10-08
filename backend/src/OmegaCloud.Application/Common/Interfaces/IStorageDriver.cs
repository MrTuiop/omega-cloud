using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Application.Common.Interfaces;

public interface IStorageDriver
{
    string DriverType { get; }
    Task<Stream> OpenReadAsync(StorageBlob blob, CancellationToken ct = default);
    Task<StorageBlobResult> WriteAsync(Stream contentStream, StorageWriteOptions options, CancellationToken ct = default);
    Task DeleteAsync(StorageBlob blob, CancellationToken ct = default);
    Task<PreSignedUrlResult?> TryGenerateDirectDownloadUrlAsync(StorageBlob blob, TimeSpan validFor, CancellationToken ct = default);
    Task<PreSignedUrlResult?> TryGenerateDirectUploadUrlAsync(UploadSession session, int chunkIndex, TimeSpan validFor, CancellationToken ct = default);
    Task<bool> PingAsync(CancellationToken ct = default);
}
