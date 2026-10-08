using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Application.Common.Interfaces;

public interface IJwtProvider
{
    string GenerateAccessToken(User user, Guid sessionId);
    string GenerateRefreshToken();
    string HashRefreshToken(string refreshToken);
    int AccessTokenExpirationSeconds { get; }
    int RefreshTokenExpirationDays { get; }
}
