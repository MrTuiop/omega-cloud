using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Application.DTOs.Files;

namespace OmegaCloud.Application.Services;

public interface IFileService
{
    Task<FileItemDto> UploadFileAsync(UploadFileStreamRequest request, Guid userId, CancellationToken ct = default);
    Task<FileMetadataDto> GetFileMetadataAsync(Guid fileId, Guid userId, CancellationToken ct = default);
    Task<FileDownloadResult> GetFileDownloadStreamAsync(Guid fileId, Guid userId, CancellationToken ct = default);
    Task<FileItemDto> UpdateFileAsync(Guid fileId, UpdateFileRequest request, Guid userId, CancellationToken ct = default);
    Task DeleteFileAsync(Guid fileId, Guid userId, CancellationToken ct = default);
}
