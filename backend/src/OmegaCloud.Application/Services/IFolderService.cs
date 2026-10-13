using OmegaCloud.Application.DTOs.Folders;

namespace OmegaCloud.Application.Services;

public interface IFolderService
{
    Task<FolderContentDto> GetFolderContentAsync(Guid? folderId, Guid userId, CancellationToken ct = default);
    Task<FolderTreeResponseDto> GetFolderTreeAsync(Guid userId, CancellationToken ct = default);
    Task<FolderDto> CreateFolderAsync(CreateFolderRequest request, Guid userId, CancellationToken ct = default);
    Task<FolderDto> UpdateFolderAsync(Guid id, UpdateFolderRequest request, Guid userId, CancellationToken ct = default);
    Task DeleteFolderAsync(Guid id, Guid userId, CancellationToken ct = default);
}
