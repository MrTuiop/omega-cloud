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
    Task<IReadOnlyList<string>> CheckConflictsAsync(CheckFileConflictsRequest request, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<FileConflictDetailDto>> CheckConflictDetailsAsync(CheckFileConflictsRequest request, Guid userId, CancellationToken ct = default);
    Task<OmegaCloud.Application.Common.Interfaces.ThumbnailResult> GetFileThumbnailAsync(Guid fileId, Guid? userId, int width = 256, int height = 256, string format = "webp", CancellationToken ct = default);
    Task<FileDownloadResult> GetFileStreamAsync(Guid fileId, Guid? userId, CancellationToken ct = default);
    Task<FileDownloadResult> DownloadFilesZipAsync(IEnumerable<Guid> fileIds, IEnumerable<Guid>? folderIds, Guid userId, CancellationToken ct = default);
    Task BulkDeleteAsync(IEnumerable<Guid>? fileIds, IEnumerable<Guid>? folderIds, Guid userId, CancellationToken ct = default);
    Task BulkMoveAsync(IEnumerable<Guid>? fileIds, IEnumerable<Guid>? folderIds, Guid? targetFolderId, Guid userId, CancellationToken ct = default);
    Task CopyToSharedAsync(IEnumerable<Guid>? fileIds, IEnumerable<Guid>? folderIds, Guid? targetSharedFolderId, Guid userId, CancellationToken ct = default);
    Task SaveToPersonalAsync(IEnumerable<Guid>? fileIds, IEnumerable<Guid>? folderIds, Guid? targetFolderId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<FileItemDto>> GetRecentFilesAsync(Guid userId, int limit = 10, CancellationToken ct = default);
    Task<OmegaCloud.Application.DTOs.Folders.FolderContentDto> SearchAsync(string query, Guid? folderId, string scope, Guid userId, Guid[]? folderIds = null, CancellationToken ct = default);
}

