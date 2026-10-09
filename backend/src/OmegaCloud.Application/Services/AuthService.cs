using Microsoft.EntityFrameworkCore;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Auth;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Application.Services;

public class AuthService : IAuthService
{
    private readonly IAppDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtProvider _jwtProvider;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IRealtimeNotifier _notifier;

    public AuthService(
        IAppDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtProvider jwtProvider,
        IDateTimeProvider dateTimeProvider,
        IRealtimeNotifier? notifier = null)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtProvider = jwtProvider;
        _dateTimeProvider = dateTimeProvider;
        _notifier = notifier ?? NullRealtimeNotifier.Instance;
    }

    public async Task<LoginResponse> LoginAsync(
        LoginRequest request,
        string ipAddress,
        string userAgent,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail))
        {
            throw new ValidationException("Username or email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ValidationException("Password is required.");
        }

        var normalizedInput = request.UsernameOrEmail.Trim().ToLower();
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == normalizedInput ||
                                      u.Email.ToLower() == normalizedInput, ct);

        if (user == null || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Invalid username/email or password.");
        }

        if (!user.IsActive)
        {
            throw new UnauthorizedException("User account is deactivated.");
        }

        var deviceName = string.IsNullOrWhiteSpace(request.DeviceName)
            ? ResolveDeviceName(userAgent)
            : request.DeviceName.Trim();

        var rawRefreshToken = _jwtProvider.GenerateRefreshToken();
        var refreshTokenHash = _jwtProvider.HashRefreshToken(rawRefreshToken);
        var now = _dateTimeProvider.UtcNow;

        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RefreshTokenHash = refreshTokenHash,
            DeviceName = deviceName,
            IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress,
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent,
            CreatedAt = now,
            LastActiveAt = now,
            ExpiresAt = now.AddDays(_jwtProvider.RefreshTokenExpirationDays),
            RevokedAt = null,
            ReplacedByTokenHash = null
        };

        _dbContext.UserSessions.Add(session);
        await _dbContext.SaveChangesAsync(ct);

        var accessToken = _jwtProvider.GenerateAccessToken(user, session.Id);

        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken,
            ExpiresInSeconds = _jwtProvider.AccessTokenExpirationSeconds,
            User = MapToUserProfileDto(user)
        };
    }

    public async Task<RefreshTokenResponse> RefreshTokenAsync(
        string rawRefreshToken,
        string ipAddress,
        string userAgent,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            throw new UnauthorizedException("Refresh token is required.");
        }

        var tokenHash = _jwtProvider.HashRefreshToken(rawRefreshToken);

        var session = await _dbContext.UserSessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.RefreshTokenHash == tokenHash, ct);

        if (session == null)
        {
            throw new UnauthorizedException("Invalid refresh token.");
        }

        var now = _dateTimeProvider.UtcNow;

        // Security check: Refresh Token Reuse Detection
        // If the token was already replaced by a newer token, this is a token compromise / replay attack!
        if (session.ReplacedByTokenHash != null)
        {
            // Revoke all active sessions of this user
            var activeSessions = await _dbContext.UserSessions
                .Where(s => s.UserId == session.UserId && s.RevokedAt == null)
                .ToListAsync(ct);

            foreach (var activeSession in activeSessions)
            {
                activeSession.RevokedAt = now;
            }

            await _dbContext.SaveChangesAsync(ct);
            await _notifier.NotifyForceLogoutAsync(session.UserId, "Security alert: sessions terminated due to suspicious token reuse.", null, ct);
            throw new UnauthorizedException("Refresh token reuse detected. All active sessions have been terminated for security.");
        }

        // If the session was explicitly revoked (e.g. via logout or remote session deletion), reject without false-positive wipe of other devices
        if (session.RevokedAt != null)
        {
            throw new UnauthorizedException("Session has been revoked.");
        }

        if (session.ExpiresAt <= now)
        {
            session.RevokedAt = now;
            await _dbContext.SaveChangesAsync(ct);
            throw new UnauthorizedException("Refresh token has expired.");
        }

        if (session.User == null || !session.User.IsActive)
        {
            throw new UnauthorizedException("User account is inactive.");
        }

        // Generate rotated token pair
        var newRefreshToken = _jwtProvider.GenerateRefreshToken();
        var newRefreshTokenHash = _jwtProvider.HashRefreshToken(newRefreshToken);

        session.RevokedAt = now;
        session.ReplacedByTokenHash = newRefreshTokenHash;
        session.LastActiveAt = now;

        var newSession = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = session.UserId,
            RefreshTokenHash = newRefreshTokenHash,
            DeviceName = session.DeviceName,
            IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? session.IpAddress : ipAddress,
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? session.UserAgent : userAgent,
            CreatedAt = session.CreatedAt, // Preserve initial device session login time
            LastActiveAt = now,
            ExpiresAt = now.AddDays(_jwtProvider.RefreshTokenExpirationDays),
            RevokedAt = null,
            ReplacedByTokenHash = null
        };

        _dbContext.UserSessions.Add(newSession);
        await _dbContext.SaveChangesAsync(ct);

        var newAccessToken = _jwtProvider.GenerateAccessToken(session.User, newSession.Id);

        return new RefreshTokenResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            ExpiresInSeconds = _jwtProvider.AccessTokenExpirationSeconds
        };
    }

    public async Task LogoutAsync(
        Guid userId,
        Guid? currentSessionId,
        string? rawRefreshToken,
        CancellationToken ct = default)
    {
        var now = _dateTimeProvider.UtcNow;
        var hasChanges = false;
        UserSession? sessionByHash = null;

        // If user is authenticated with a valid current session
        if (currentSessionId.HasValue && userId != Guid.Empty)
        {
            var session = await _dbContext.UserSessions
                .FirstOrDefaultAsync(s => s.Id == currentSessionId.Value && s.UserId == userId, ct);

            if (session != null && session.RevokedAt == null)
            {
                session.RevokedAt = now;
                hasChanges = true;
            }
        }

        // If refresh token is provided (via cookie or request body)
        if (!string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            var tokenHash = _jwtProvider.HashRefreshToken(rawRefreshToken);
            sessionByHash = await _dbContext.UserSessions
                .FirstOrDefaultAsync(s => s.RefreshTokenHash == tokenHash && (userId == Guid.Empty || s.UserId == userId), ct);

            if (sessionByHash != null && sessionByHash.RevokedAt == null)
            {
                sessionByHash.RevokedAt = now;
                hasChanges = true;
            }
        }

        if (hasChanges)
        {
            await _dbContext.SaveChangesAsync(ct);

            var affectedSessionId = currentSessionId ?? sessionByHash?.Id;
            var affectedUserId = userId != Guid.Empty ? userId : sessionByHash?.UserId ?? Guid.Empty;

            if (affectedSessionId.HasValue && affectedUserId != Guid.Empty)
            {
                await _notifier.NotifyForceLogoutAsync(affectedUserId, "Logged out.", affectedSessionId.Value, ct);
            }
        }
    }

    public async Task<IReadOnlyList<UserSessionDto>> GetUserSessionsAsync(
        Guid userId,
        Guid? currentSessionId,
        CancellationToken ct = default)
    {
        var now = _dateTimeProvider.UtcNow;
        var sessions = await _dbContext.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastActiveAt)
            .ToListAsync(ct);

        if (currentSessionId.HasValue)
        {
            var currentSession = sessions.FirstOrDefault(s => s.Id == currentSessionId.Value);
            if (currentSession != null)
            {
                currentSession.LastActiveAt = now;
                await _dbContext.SaveChangesAsync(ct);
            }
        }

        return sessions.Select(s => new UserSessionDto
        {
            Id = s.Id,
            DeviceName = s.DeviceName,
            IpAddress = s.IpAddress,
            UserAgent = s.UserAgent,
            CreatedAt = s.CreatedAt,
            LastActiveAt = s.LastActiveAt,
            IsCurrent = currentSessionId.HasValue && s.Id == currentSessionId.Value
        }).ToList();
    }

    public async Task RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken ct = default)
    {
        var session = await _dbContext.UserSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);

        if (session == null)
        {
            throw new NotFoundException("Session", sessionId);
        }

        if (session.RevokedAt == null)
        {
            session.RevokedAt = _dateTimeProvider.UtcNow;
            await _dbContext.SaveChangesAsync(ct);
            await _notifier.NotifyForceLogoutAsync(userId, "Session was revoked.", sessionId, ct);
        }
    }

    public async Task<UserProfileDto> GetCurrentUserProfileAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user == null)
        {
            throw new NotFoundException("User", userId);
        }

        return MapToUserProfileDto(user);
    }

    private static UserProfileDto MapToUserProfileDto(User user)
    {
        return new UserProfileDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role.ToString(),
            AllocatedQuotaBytes = user.AllocatedQuotaBytes,
            UsedQuotaBytes = user.UsedQuotaBytes,
            CreatedAt = user.CreatedAt
        };
    }

    private static string ResolveDeviceName(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "Unknown Device";
        }

        var os = "Device";
        if (userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase))
            os = "Windows PC";
        else if (userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("Mac OS", StringComparison.OrdinalIgnoreCase))
            os = "Mac";
        else if (userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
            os = "iPhone";
        else if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase))
            os = "iPad";
        else if (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase))
            os = "Android Device";
        else if (userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase))
            os = "Linux PC";

        var browser = "Browser";
        if (userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase))
            browser = "Edge";
        else if (userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase))
            browser = "Chrome";
        else if (userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase) && !userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase))
            browser = "Safari";
        else if (userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase))
            browser = "Firefox";

        return $"{os} ({browser})";
    }
}
