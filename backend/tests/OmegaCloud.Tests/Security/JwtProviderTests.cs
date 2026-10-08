using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;
using OmegaCloud.Infrastructure.Security;

namespace OmegaCloud.Tests.Security;

public class JwtProviderTests
{
    private class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    private readonly JwtOptions _options = new()
    {
        Secret = "test_super_secret_jwt_key_at_least_32_bytes_long_123456",
        Issuer = "OmegaCloudTestIssuer",
        Audience = "OmegaCloudTestAudience",
        AccessTokenExpirationMinutes = 15,
        RefreshTokenExpirationDays = 30
    };

    private readonly FakeDateTimeProvider _dateTimeProvider = new();

    [Fact]
    public void GenerateAccessToken_ShouldContainRequiredClaims()
    {
        var jwtProvider = new JwtProvider(Options.Create(_options), _dateTimeProvider);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "alexander",
            Email = "alexander@omega.local",
            Role = UserRole.Admin
        };
        var sessionId = Guid.NewGuid();

        var tokenString = jwtProvider.GenerateAccessToken(user, sessionId);

        Assert.NotNull(tokenString);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(tokenString);

        Assert.Equal(_options.Issuer, token.Issuer);
        Assert.Contains(_options.Audience, token.Audiences);

        var subClaim = token.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub);
        Assert.NotNull(subClaim);
        Assert.Equal(user.Id.ToString(), subClaim.Value);

        var roleClaim = token.Claims.FirstOrDefault(c => c.Type == "role" || c.Type == ClaimTypes.Role);
        Assert.NotNull(roleClaim);
        Assert.Equal(UserRole.Admin.ToString(), roleClaim.Value);

        var sessionClaim = token.Claims.FirstOrDefault(c => c.Type == "session_id");
        Assert.NotNull(sessionClaim);
        Assert.Equal(sessionId.ToString(), sessionClaim.Value);

        Assert.Equal(_dateTimeProvider.UtcNow.AddMinutes(15), token.ValidTo);
    }

    [Fact]
    public void GenerateRefreshToken_ShouldReturnUniqueSecureStrings()
    {
        var jwtProvider = new JwtProvider(Options.Create(_options), _dateTimeProvider);

        var token1 = jwtProvider.GenerateRefreshToken();
        var token2 = jwtProvider.GenerateRefreshToken();

        Assert.NotNull(token1);
        Assert.NotNull(token2);
        Assert.NotEqual(token1, token2);
        Assert.True(token1.Length >= 64);
    }

    [Fact]
    public void HashRefreshToken_ShouldReturnDeterministicSha256Hex()
    {
        var jwtProvider = new JwtProvider(Options.Create(_options), _dateTimeProvider);
        var rawToken = "my-secure-sample-refresh-token";

        var hash1 = jwtProvider.HashRefreshToken(rawToken);
        var hash2 = jwtProvider.HashRefreshToken(rawToken);

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // 32 bytes in hex = 64 characters
    }
}
