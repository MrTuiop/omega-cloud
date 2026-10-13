using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Application.DTOs.Folders;

namespace OmegaCloud.Application.Services;

public interface ISharedService
{
    Task<FolderContentDto> GetSharedFolderContentAsync(Guid? folderId, Guid? currentUserId = null, CancellationToken ct = default);
    Task<FileMetadataDto> GetSharedFileMetadataAsync(Guid fileId, CancellationToken ct = default);
    Task<FileDownloadResult> GetSharedFileDownloadStreamAsync(Guid fileId, CancellationToken ct = default);
    Task<FileItemDto> UploadSharedFileAsync(UploadFileStreamRequest request, Guid userId, CancellationToken ct = default);
    Task DeleteSharedFileAsync(Guid fileId, Guid userId, string? userRole, CancellationToken ct = default);
    Task DeleteSharedFolderAsync(Guid folderId, Guid userId, string? userRole, CancellationToken ct = default);
    Task<FolderDto> CreateSharedFolderAsync(CreateFolderRequest request, Guid userId, CancellationToken ct = default);
    Task CopyToSharedAsync(IEnumerable<Guid>? fileIds, IEnumerable<Guid>? folderIds, Guid? targetSharedFolderId, Guid userId, CancellationToken ct = default);
    Task SaveToPersonalAsync(IEnumerable<Guid>? fileIds, IEnumerable<Guid>? folderIds, Guid? targetFolderId, Guid userId, CancellationToken ct = default);
}

