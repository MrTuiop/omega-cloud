using OmegaCloud.Application.DTOs.Admin;

namespace OmegaCloud.Application.Services;

public interface IAdminUserService
{
    Task<IReadOnlyList<AdminUserDto>> GetAllUsersAsync(CancellationToken ct = default);
    Task<AdminUserDetailDto> GetUserByIdAsync(Guid id, CancellationToken ct = default);
    Task<AdminUserDto> CreateUserAsync(CreateUserRequest request, string? ipAddress = null, CancellationToken ct = default);
    Task<AdminUserDto> UpdateUserAsync(Guid id, UpdateUserRequest request, string? ipAddress = null, CancellationToken ct = default);
    Task ResetPasswordAsync(Guid id, AdminResetPasswordRequest request, string? ipAddress = null, CancellationToken ct = default);
    Task RevokeUserSessionsAsync(Guid id, string? ipAddress = null, CancellationToken ct = default);
    Task RevokeUserSessionAsync(Guid userId, Guid sessionId, string? ipAddress = null, CancellationToken ct = default);
    Task DeleteUserAsync(Guid id, string? ipAddress = null, CancellationToken ct = default);
}
