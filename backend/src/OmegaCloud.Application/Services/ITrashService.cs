using OmegaCloud.Application.DTOs.Trash;

namespace OmegaCloud.Application.Services;

public interface ITrashService
{
    Task<TrashContentDto> GetTrashContentAsync(Guid userId, bool topLevelOnly = false, CancellationToken ct = default);
    Task<RestoreItemResultDto> RestoreItemAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task PermanentlyDeleteItemAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<EmptyTrashResultDto> EmptyTrashAsync(Guid userId, CancellationToken ct = default);
    Task<TrashCleanupResultDto> CleanupExpiredTrashAsync(TimeSpan olderThan, CancellationToken ct = default);
}
