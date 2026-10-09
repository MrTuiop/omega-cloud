using OmegaCloud.Application.DTOs.Admin;

namespace OmegaCloud.Application.Services;

public interface ISystemHealthService
{
    Task<SystemHealthDto> GetSystemHealthAsync(CancellationToken ct = default);
}
