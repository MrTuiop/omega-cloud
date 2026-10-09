using OmegaCloud.Application.DTOs.Upload;

namespace OmegaCloud.Application.Services;

public interface IUploadGcService
{
    Task<UploadGcResultDto> CleanupExpiredSessionsAsync(TimeSpan? removeSessionsOlderThan = null, CancellationToken ct = default);
}
