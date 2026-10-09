using OmegaCloud.Application.DTOs.Admin;

namespace OmegaCloud.Application.Services;

public interface IAuditService
{
    Task LogAsync(
        string action,
        string resource,
        string? resourceId = null,
        Guid? userId = null,
        string? username = null,
        string? ipAddress = null,
        object? details = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<AuditLogDto>> GetLogsAsync(
        Guid? userId = null,
        string? action = null,
        string? search = null,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default);
}
