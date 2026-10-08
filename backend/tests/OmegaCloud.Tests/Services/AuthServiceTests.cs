using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Auth;
using OmegaCloud.Application.Services;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;
using OmegaCloud.Infrastructure.Data;
using OmegaCloud.Infrastructure.Security;

namespace OmegaCloud.Tests.Services;

public class AuthServiceTests
{
    private class TestDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    }

    private readonly TestDateTimeProvider _dateTimeProvider = new();
    private readonly Argon2PasswordHasher _passwordHasher = new();
    private readonly JwtProvider _jwtProvider;

    public AuthServiceTests()
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            Secret = "test_super_secret_jwt_key_at_least_32_bytes_long_123456",
            Issuer = "OmegaCloudTest",
            Audience = "OmegaCloudTest",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 30
        });

        _jwtProvider = new JwtProvider(jwtOptions, _dateTimeProvider);
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task LoginAsync_WithValidUsernameAndPassword_ShouldSucceed()
    {
        using var db = CreateDbContext();
        var rawPassword = "TestPassword123!";
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser",
            Email = "test@omega.local",
            PasswordHash = _passwordHasher.HashPassword(rawPassword),
            Role = UserRole.User,
            AllocatedQuotaBytes = 50_000_000,
            UsedQuotaBytes = 1_000,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        var response = await service.LoginAsync(
            new LoginRequest { UsernameOrEmail = "testuser", Password = rawPassword, DeviceName = "Test Laptop" },
            "192.168.1.100",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0");

        Assert.NotNull(response.AccessToken);
        Assert.NotNull(response.RefreshToken);
        Assert.Equal(user.Username, response.User.Username);
        Assert.Equal(user.AllocatedQuotaBytes, response.User.AllocatedQuotaBytes);

        var sessionInDb = await db.UserSessions.FirstOrDefaultAsync(s => s.UserId == user.Id);
        Assert.NotNull(sessionInDb);
        Assert.Equal("Test Laptop", sessionInDb.DeviceName);
        Assert.Equal("192.168.1.100", sessionInDb.IpAddress);
        Assert.Null(sessionInDb.RevokedAt);
    }

    [Fact]
    public async Task LoginAsync_WithEmail_ShouldSucceed()
    {
        using var db = CreateDbContext();
        var rawPassword = "Password987!";
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "userbyemail",
            Email = "loginbyemail@omega.local",
            PasswordHash = _passwordHasher.HashPassword(rawPassword),
            Role = UserRole.User,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        var response = await service.LoginAsync(
            new LoginRequest { UsernameOrEmail = "loginbyemail@omega.local", Password = rawPassword },
            "127.0.0.1",
            "Mozilla/5.0");

        Assert.NotNull(response.AccessToken);
        Assert.Equal(user.Email, response.User.Email);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ShouldThrowUnauthorizedException()
    {
        using var db = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "wrongpassuser",
            Email = "wp@omega.local",
            PasswordHash = _passwordHasher.HashPassword("RealPassword!"),
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.LoginAsync(new LoginRequest { UsernameOrEmail = "wrongpassuser", Password = "BadPassword!" }, "127.0.0.1", "agent"));
    }

    [Fact]
    public async Task LoginAsync_WithDeactivatedUser_ShouldThrowUnauthorizedException()
    {
        using var db = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "deactivated",
            Email = "deactivated@omega.local",
            PasswordHash = _passwordHasher.HashPassword("Pass123!"),
            IsActive = false
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.LoginAsync(new LoginRequest { UsernameOrEmail = "deactivated", Password = "Pass123!" }, "127.0.0.1", "agent"));
    }

    [Fact]
    public async Task RefreshTokenAsync_ValidToken_ShouldRotateTokenAndRevokeOldSession()
    {
        using var db = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "refresher",
            Email = "refresher@omega.local",
            PasswordHash = _passwordHasher.HashPassword("Pass123!"),
            IsActive = true
        };
        db.Users.Add(user);

        var oldRefreshToken = _jwtProvider.GenerateRefreshToken();
        var oldTokenHash = _jwtProvider.HashRefreshToken(oldRefreshToken);

        var oldSession = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RefreshTokenHash = oldTokenHash,
            DeviceName = "MacBook Pro",
            IpAddress = "10.0.0.1",
            UserAgent = "Safari",
            CreatedAt = _dateTimeProvider.UtcNow,
            ExpiresAt = _dateTimeProvider.UtcNow.AddDays(30),
            LastActiveAt = _dateTimeProvider.UtcNow
        };
        db.UserSessions.Add(oldSession);
        await db.SaveChangesAsync();

        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        // Advance clock by 1 hour
        _dateTimeProvider.UtcNow = _dateTimeProvider.UtcNow.AddHours(1);

        var refreshResponse = await service.RefreshTokenAsync(oldRefreshToken, "10.0.0.2", "Safari-Updated");

        Assert.NotNull(refreshResponse.AccessToken);
        Assert.NotNull(refreshResponse.RefreshToken);
        Assert.NotEqual(oldRefreshToken, refreshResponse.RefreshToken);

        // Verify old session was revoked and replaced
        var updatedOldSession = await db.UserSessions.FindAsync(oldSession.Id);
        Assert.NotNull(updatedOldSession!.RevokedAt);
        Assert.NotNull(updatedOldSession.ReplacedByTokenHash);

        var newSessionHash = _jwtProvider.HashRefreshToken(refreshResponse.RefreshToken);
        Assert.Equal(newSessionHash, updatedOldSession.ReplacedByTokenHash);

        // Verify new session is active
        var newSession = await db.UserSessions.FirstOrDefaultAsync(s => s.RefreshTokenHash == newSessionHash);
        Assert.NotNull(newSession);
        Assert.Null(newSession.RevokedAt);
        Assert.Equal("10.0.0.2", newSession.IpAddress);
    }

    [Fact]
    public async Task RefreshTokenAsync_ReusingRotatedToken_ShouldDetectCompromiseAndRevokeAllSessions()
    {
        using var db = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "compromised_user",
            Email = "compromised@omega.local",
            PasswordHash = _passwordHasher.HashPassword("Pass123!"),
            IsActive = true
        };
        db.Users.Add(user);

        // Session 1: already rotated / revoked
        var stolenToken = _jwtProvider.GenerateRefreshToken();
        var stolenTokenHash = _jwtProvider.HashRefreshToken(stolenToken);

        var session1 = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RefreshTokenHash = stolenTokenHash,
            DeviceName = "Session1",
            IpAddress = "1.1.1.1",
            UserAgent = "Agent1",
            CreatedAt = _dateTimeProvider.UtcNow,
            ExpiresAt = _dateTimeProvider.UtcNow.AddDays(30),
            LastActiveAt = _dateTimeProvider.UtcNow,
            RevokedAt = _dateTimeProvider.UtcNow, // Already revoked!
            ReplacedByTokenHash = "some_newer_hash"
        };

        // Session 2: active session on another device
        var session2 = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RefreshTokenHash = "legit_active_hash",
            DeviceName = "Session2-Active",
            IpAddress = "2.2.2.2",
            UserAgent = "Agent2",
            CreatedAt = _dateTimeProvider.UtcNow,
            ExpiresAt = _dateTimeProvider.UtcNow.AddDays(30),
            LastActiveAt = _dateTimeProvider.UtcNow,
            RevokedAt = null
        };

        db.UserSessions.AddRange(session1, session2);
        await db.SaveChangesAsync();

        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        // Attacker attempts to use stolenToken
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.RefreshTokenAsync(stolenToken, "6.6.6.6", "HackerBot"));

        Assert.Contains("reuse detected", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Verify that ALL sessions including session2 are now REVOKED
        var reloadedSession2 = await db.UserSessions.FindAsync(session2.Id);
        Assert.NotNull(reloadedSession2!.RevokedAt);
    }

    [Fact]
    public async Task RefreshTokenAsync_ExpiredToken_ShouldThrowUnauthorizedException()
    {
        using var db = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "expired_user",
            Email = "exp@omega.local",
            PasswordHash = _passwordHasher.HashPassword("Pass123!"),
            IsActive = true
        };
        db.Users.Add(user);

        var expiredToken = _jwtProvider.GenerateRefreshToken();
        var expiredTokenHash = _jwtProvider.HashRefreshToken(expiredToken);

        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RefreshTokenHash = expiredTokenHash,
            CreatedAt = _dateTimeProvider.UtcNow.AddDays(-31),
            ExpiresAt = _dateTimeProvider.UtcNow.AddDays(-1), // Expired!
            LastActiveAt = _dateTimeProvider.UtcNow.AddDays(-10)
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();

        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.RefreshTokenAsync(expiredToken, "1.1.1.1", "Agent"));
    }

    [Fact]
    public async Task GetUserSessionsAsync_ShouldReturnOnlyActiveSessionsWithCurrentMarked()
    {
        using var db = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "multidevice",
            Email = "multi@omega.local",
            PasswordHash = "hash",
            IsActive = true
        };
        db.Users.Add(user);

        var currentSessionId = Guid.NewGuid();
        var otherSessionId = Guid.NewGuid();
        var revokedSessionId = Guid.NewGuid();

        var sessionCurrent = new UserSession
        {
            Id = currentSessionId,
            UserId = user.Id,
            RefreshTokenHash = "hash1",
            DeviceName = "Current Phone",
            CreatedAt = _dateTimeProvider.UtcNow,
            ExpiresAt = _dateTimeProvider.UtcNow.AddDays(30),
            LastActiveAt = _dateTimeProvider.UtcNow
        };

        var sessionOther = new UserSession
        {
            Id = otherSessionId,
            UserId = user.Id,
            RefreshTokenHash = "hash2",
            DeviceName = "Work Desktop",
            CreatedAt = _dateTimeProvider.UtcNow.AddDays(-5),
            ExpiresAt = _dateTimeProvider.UtcNow.AddDays(25),
            LastActiveAt = _dateTimeProvider.UtcNow.AddHours(-2)
        };

        var sessionRevoked = new UserSession
        {
            Id = revokedSessionId,
            UserId = user.Id,
            RefreshTokenHash = "hash3",
            DeviceName = "Old Tablet",
            CreatedAt = _dateTimeProvider.UtcNow.AddDays(-20),
            ExpiresAt = _dateTimeProvider.UtcNow.AddDays(10),
            LastActiveAt = _dateTimeProvider.UtcNow.AddDays(-15),
            RevokedAt = _dateTimeProvider.UtcNow.AddDays(-10)
        };

        db.UserSessions.AddRange(sessionCurrent, sessionOther, sessionRevoked);
        await db.SaveChangesAsync();

        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        var sessions = await service.GetUserSessionsAsync(user.Id, currentSessionId);

        Assert.Equal(2, sessions.Count); // Only 2 active sessions
        var current = sessions.First(s => s.Id == currentSessionId);
        Assert.True(current.IsCurrent);

        var other = sessions.First(s => s.Id == otherSessionId);
        Assert.False(other.IsCurrent);
    }

    [Fact]
    public async Task RevokeSessionAsync_ShouldMarkSessionRevoked()
    {
        using var db = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "revoker",
            Email = "revoker@omega.local",
            PasswordHash = "hash",
            IsActive = true
        };
        db.Users.Add(user);

        var sessionId = Guid.NewGuid();
        var session = new UserSession
        {
            Id = sessionId,
            UserId = user.Id,
            RefreshTokenHash = "hash1",
            CreatedAt = _dateTimeProvider.UtcNow,
            ExpiresAt = _dateTimeProvider.UtcNow.AddDays(30),
            LastActiveAt = _dateTimeProvider.UtcNow
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();

        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        await service.RevokeSessionAsync(user.Id, sessionId);

        var reloaded = await db.UserSessions.FindAsync(sessionId);
        Assert.NotNull(reloaded!.RevokedAt);
    }

    [Fact]
    public async Task RevokeSessionAsync_ForeignUserSession_ShouldThrowNotFoundException()
    {
        using var db = CreateDbContext();
        var user1 = new User { Id = Guid.NewGuid(), Username = "u1", Email = "u1@o.l", PasswordHash = "h", IsActive = true };
        var user2 = new User { Id = Guid.NewGuid(), Username = "u2", Email = "u2@o.l", PasswordHash = "h", IsActive = true };
        db.Users.AddRange(user1, user2);

        var session2 = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user2.Id,
            RefreshTokenHash = "h2",
            CreatedAt = _dateTimeProvider.UtcNow,
            ExpiresAt = _dateTimeProvider.UtcNow.AddDays(30)
        };
        db.UserSessions.Add(session2);
        await db.SaveChangesAsync();

        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        // User 1 tries to revoke user 2's session
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.RevokeSessionAsync(user1.Id, session2.Id));
    }

    [Fact]
    public async Task LoginAsync_WithWhitespacePadding_ShouldTrimAndSucceed()
    {
        using var db = CreateDbContext();
        var rawPassword = "Password123!";
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "paddeduser",
            Email = "padded@omega.local",
            PasswordHash = _passwordHasher.HashPassword(rawPassword),
            Role = UserRole.User,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        var response = await service.LoginAsync(
            new LoginRequest { UsernameOrEmail = "  paddeduser  ", Password = rawPassword },
            "127.0.0.1",
            "Agent");

        Assert.NotNull(response.AccessToken);
        Assert.Equal("paddeduser", response.User.Username);
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("   ", "password")]
    [InlineData("user", "")]
    [InlineData("user", "   ")]
    public async Task LoginAsync_WithEmptyCredentials_ShouldThrowValidationException(string userOrEmail, string password)
    {
        using var db = CreateDbContext();
        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.LoginAsync(new LoginRequest { UsernameOrEmail = userOrEmail, Password = password }, "127.0.0.1", "agent"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RefreshTokenAsync_WithEmptyToken_ShouldThrowUnauthorizedException(string token)
    {
        using var db = CreateDbContext();
        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.RefreshTokenAsync(token, "127.0.0.1", "agent"));
    }

    [Fact]
    public async Task RefreshTokenAsync_WithNonExistentToken_ShouldThrowUnauthorizedException()
    {
        using var db = CreateDbContext();
        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.RefreshTokenAsync("non_existent_refresh_token_string", "127.0.0.1", "agent"));
    }

    [Fact]
    public async Task RefreshTokenAsync_WithDeactivatedUser_ShouldThrowUnauthorizedException()
    {
        using var db = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "deact_refresher",
            Email = "dr@omega.local",
            PasswordHash = "hash",
            IsActive = false
        };
        db.Users.Add(user);

        var rawToken = _jwtProvider.GenerateRefreshToken();
        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RefreshTokenHash = _jwtProvider.HashRefreshToken(rawToken),
            CreatedAt = _dateTimeProvider.UtcNow,
            ExpiresAt = _dateTimeProvider.UtcNow.AddDays(30),
            LastActiveAt = _dateTimeProvider.UtcNow
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();

        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.RefreshTokenAsync(rawToken, "127.0.0.1", "agent"));
    }

    [Fact]
    public async Task GetCurrentUserProfileAsync_ExistingUser_ShouldReturnCorrectProfile()
    {
        using var db = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "profileuser",
            Email = "profile@omega.local",
            PasswordHash = "hash",
            Role = UserRole.Admin,
            AllocatedQuotaBytes = 200_000_000,
            UsedQuotaBytes = 50_000_000,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        var profile = await service.GetCurrentUserProfileAsync(user.Id);

        Assert.Equal(user.Id, profile.Id);
        Assert.Equal("profileuser", profile.Username);
        Assert.Equal("Admin", profile.Role);
        Assert.Equal(200_000_000, profile.AllocatedQuotaBytes);
        Assert.Equal(50_000_000, profile.UsedQuotaBytes);
    }

    [Fact]
    public async Task GetCurrentUserProfileAsync_NonExistingUser_ShouldThrowNotFoundException()
    {
        using var db = CreateDbContext();
        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetCurrentUserProfileAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task LogoutAsync_WithAnonymousUserIdAndRefreshToken_ShouldRevokeSession()
    {
        using var db = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "anonlogoutuser",
            Email = "anon@omega.local",
            PasswordHash = "hash",
            IsActive = true
        };
        db.Users.Add(user);

        var rawToken = _jwtProvider.GenerateRefreshToken();
        var tokenHash = _jwtProvider.HashRefreshToken(rawToken);

        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RefreshTokenHash = tokenHash,
            CreatedAt = _dateTimeProvider.UtcNow,
            ExpiresAt = _dateTimeProvider.UtcNow.AddDays(30),
            LastActiveAt = _dateTimeProvider.UtcNow,
            RevokedAt = null
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();

        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        // Client calls logout without access token (userId == Guid.Empty), but providing rawRefreshToken
        await service.LogoutAsync(Guid.Empty, null, rawToken);

        var reloadedSession = await db.UserSessions.FindAsync(session.Id);
        Assert.NotNull(reloadedSession);
        Assert.NotNull(reloadedSession.RevokedAt);
        Assert.Equal(_dateTimeProvider.UtcNow, reloadedSession.RevokedAt);
    }

    [Fact]
    public async Task RefreshTokenAsync_RevokedWithoutReplacement_ShouldThrowUnauthorizedAndNotRevokeOtherSessions()
    {
        using var db = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "isolated_revocation_user",
            Email = "iso@omega.local",
            PasswordHash = "hash",
            IsActive = true
        };
        db.Users.Add(user);

        // Session 1: logged out / revoked directly (ReplacedByTokenHash is null)
        var revokedToken = _jwtProvider.GenerateRefreshToken();
        var revokedTokenHash = _jwtProvider.HashRefreshToken(revokedToken);

        var sessionRevoked = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RefreshTokenHash = revokedTokenHash,
            CreatedAt = _dateTimeProvider.UtcNow.AddDays(-1),
            ExpiresAt = _dateTimeProvider.UtcNow.AddDays(29),
            LastActiveAt = _dateTimeProvider.UtcNow.AddHours(-1),
            RevokedAt = _dateTimeProvider.UtcNow.AddHours(-1),
            ReplacedByTokenHash = null
        };

        // Session 2: active session on another device
        var activeToken = _jwtProvider.GenerateRefreshToken();
        var activeTokenHash = _jwtProvider.HashRefreshToken(activeToken);

        var sessionActive = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RefreshTokenHash = activeTokenHash,
            CreatedAt = _dateTimeProvider.UtcNow,
            ExpiresAt = _dateTimeProvider.UtcNow.AddDays(30),
            LastActiveAt = _dateTimeProvider.UtcNow,
            RevokedAt = null
        };

        db.UserSessions.AddRange(sessionRevoked, sessionActive);
        await db.SaveChangesAsync();

        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        // Attempting to refresh the revoked session should fail
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.RefreshTokenAsync(revokedToken, "1.2.3.4", "agent"));

        Assert.Equal("Session has been revoked.", ex.Message);

        // Crucial verification: active session must NOT be revoked!
        var reloadedActive = await db.UserSessions.FindAsync(sessionActive.Id);
        Assert.NotNull(reloadedActive);
        Assert.Null(reloadedActive.RevokedAt);
    }

    [Fact]
    public async Task RefreshTokenAsync_PreservesOriginalSessionCreatedAt()
    {
        using var db = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "createdatuser",
            Email = "ca@omega.local",
            PasswordHash = "hash",
            IsActive = true
        };
        db.Users.Add(user);

        var originalCreatedAt = new DateTime(2025, 12, 1, 8, 30, 0, DateTimeKind.Utc);
        var oldToken = _jwtProvider.GenerateRefreshToken();
        var oldTokenHash = _jwtProvider.HashRefreshToken(oldToken);

        var oldSession = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RefreshTokenHash = oldTokenHash,
            CreatedAt = originalCreatedAt,
            ExpiresAt = _dateTimeProvider.UtcNow.AddDays(30),
            LastActiveAt = _dateTimeProvider.UtcNow
        };
        db.UserSessions.Add(oldSession);
        await db.SaveChangesAsync();

        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        // Advance current time by 5 hours
        _dateTimeProvider.UtcNow = _dateTimeProvider.UtcNow.AddHours(5);

        var refreshResponse = await service.RefreshTokenAsync(oldToken, "10.0.0.1", "Chrome");

        var newSessionHash = _jwtProvider.HashRefreshToken(refreshResponse.RefreshToken);
        var newSession = await db.UserSessions.FirstOrDefaultAsync(s => s.RefreshTokenHash == newSessionHash);

        Assert.NotNull(newSession);
        // CreatedAt must match the original session, NOT the refresh timestamp!
        Assert.Equal(originalCreatedAt, newSession.CreatedAt);
        Assert.Equal(_dateTimeProvider.UtcNow, newSession.LastActiveAt);
    }

    [Fact]
    public async Task GetUserSessionsAsync_WithCurrentSession_ShouldUpdateLastActiveAt()
    {
        using var db = CreateDbContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "lastactiveuser",
            Email = "la@omega.local",
            PasswordHash = "hash",
            IsActive = true
        };
        db.Users.Add(user);

        var currentSessionId = Guid.NewGuid();
        var session = new UserSession
        {
            Id = currentSessionId,
            UserId = user.Id,
            RefreshTokenHash = "h1",
            CreatedAt = _dateTimeProvider.UtcNow.AddDays(-2),
            ExpiresAt = _dateTimeProvider.UtcNow.AddDays(28),
            LastActiveAt = _dateTimeProvider.UtcNow.AddDays(-1)
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();

        var service = new AuthService(db, _passwordHasher, _jwtProvider, _dateTimeProvider);

        var sessions = await service.GetUserSessionsAsync(user.Id, currentSessionId);

        Assert.Single(sessions);
        Assert.True(sessions[0].IsCurrent);
        Assert.Equal(_dateTimeProvider.UtcNow, sessions[0].LastActiveAt);

        var reloaded = await db.UserSessions.FindAsync(currentSessionId);
        Assert.Equal(_dateTimeProvider.UtcNow, reloaded!.LastActiveAt);
    }
}
