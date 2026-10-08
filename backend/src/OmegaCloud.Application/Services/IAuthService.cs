using OmegaCloud.Application.DTOs.Auth;

namespace OmegaCloud.Application.Services;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, string ipAddress, string userAgent, CancellationToken ct = default);
    Task<RefreshTokenResponse> RefreshTokenAsync(string rawRefreshToken, string ipAddress, string userAgent, CancellationToken ct = default);
    Task LogoutAsync(Guid userId, Guid? currentSessionId, string? rawRefreshToken, CancellationToken ct = default);
    Task<IReadOnlyList<UserSessionDto>> GetUserSessionsAsync(Guid userId, Guid? currentSessionId, CancellationToken ct = default);
    Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken ct = default);
    Task<UserProfileDto> GetCurrentUserProfileAsync(Guid userId, CancellationToken ct = default);
}
