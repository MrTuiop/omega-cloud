using Microsoft.EntityFrameworkCore;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Admin;
using OmegaCloud.Application.DTOs.Auth;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Application.Services;

public class AdminUserService : IAdminUserService
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IRealtimeNotifier _notifier;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _auditService;
    private readonly ISessionRevocationCache? _revocationCache;

    public AdminUserService(
        IAppDbContext db,
        IPasswordHasher passwordHasher,
        IRealtimeNotifier notifier,
        ICurrentUser currentUser,
        IAuditService auditService,
        ISessionRevocationCache? revocationCache = null)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _notifier = notifier;
        _currentUser = currentUser;
        _auditService = auditService;
        _revocationCache = revocationCache;
    }

    public async Task<IReadOnlyList<AdminUserDto>> GetAllUsersAsync(CancellationToken ct = default)
    {
        var users = await _db.Users
            .AsNoTracking()
            .Include(u => u.Sessions)
            .OrderBy(u => u.Username)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        return users.Select(u => new AdminUserDto
        {
            Id = u.Id,
            Username = u.Username,
            Email = u.Email,
            Role = u.Role.ToString(),
            AllocatedQuotaBytes = u.AllocatedQuotaBytes,
            UsedQuotaBytes = u.UsedQuotaBytes,
            IsActive = u.IsActive,
            CreatedAt = u.CreatedAt,
            UpdatedAt = u.UpdatedAt,
            LastActiveAt = u.Sessions.Any()
                ? u.Sessions.OrderByDescending(s => s.LastActiveAt).Select(s => (DateTime?)s.LastActiveAt).FirstOrDefault()
                : null,
            ActiveSessionsCount = u.Sessions.Count(s => s.RevokedAt == null && s.ExpiresAt > now)
        }).ToList();
    }

    public async Task<AdminUserDetailDto> GetUserByIdAsync(Guid id, CancellationToken ct = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Sessions)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user == null)
        {
            throw new NotFoundException($"User with ID '{id}' was not found.");
        }

        var now = DateTime.UtcNow;
        var activeSessions = user.Sessions
            .Where(s => s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastActiveAt)
            .Select(s => new UserSessionDto
            {
                Id = s.Id,
                DeviceName = s.DeviceName,
                IpAddress = s.IpAddress,
                UserAgent = s.UserAgent,
                CreatedAt = s.CreatedAt,
                LastActiveAt = s.LastActiveAt,
                IsCurrent = s.Id == _currentUser.SessionId
            })
            .ToList();

        return new AdminUserDetailDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role.ToString(),
            AllocatedQuotaBytes = user.AllocatedQuotaBytes,
            UsedQuotaBytes = user.UsedQuotaBytes,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            ActiveSessionsCount = activeSessions.Count,
            Sessions = activeSessions
        };
    }

    public async Task<AdminUserDto> CreateUserAsync(CreateUserRequest request, string? ipAddress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            throw new ValidationException("Username is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new ValidationException("Email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
        {
            throw new ValidationException("Password must be at least 6 characters long.");
        }

        var usernameNormalized = request.Username.Trim();
        var emailNormalized = request.Email.Trim().ToLowerInvariant();

        var existingUsername = await _db.Users.AnyAsync(u => u.Username.ToLower() == usernameNormalized.ToLower(), ct);
        if (existingUsername)
        {
            throw new ValidationException($"User with username '{usernameNormalized}' already exists.");
        }

        var existingEmail = await _db.Users.AnyAsync(u => u.Email.ToLower() == emailNormalized, ct);
        if (existingEmail)
        {
            throw new ValidationException($"User with email '{emailNormalized}' already exists.");
        }

        if (request.AllocatedQuotaBytes.HasValue && request.AllocatedQuotaBytes.Value < 0)
        {
            throw new ValidationException("Allocated quota cannot be negative.");
        }

        var role = UserRole.User;
        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            if (!Enum.TryParse<UserRole>(request.Role, true, out role))
            {
                throw new ValidationException($"Invalid role '{request.Role}'. Supported roles: Admin, User.");
            }
        }

        // Validate physical node capacity limit: targetQuota <= sum(ActiveNodes.MaxCapacityBytes)
        // Overprovisioning is supported: sum of user quotas can exceed cluster capacity,
        // but an individual user's quota cannot exceed total active storage nodes capacity.
        var totalActiveNodesCapacity = await _db.StorageNodes
            .Where(n => n.IsActive)
            .SumAsync(n => n.MaxCapacityBytes, ct);

        var defaultQuota = totalActiveNodesCapacity > 0
            ? Math.Min(107_374_182_400L, totalActiveNodesCapacity)
            : 107_374_182_400L;

        var targetQuota = request.AllocatedQuotaBytes ?? defaultQuota;

        if (targetQuota > totalActiveNodesCapacity)
        {
            throw new ValidationException($"Cannot allocate {targetQuota} bytes. User quota ({targetQuota} bytes) would exceed active storage nodes capacity ({totalActiveNodesCapacity} bytes).");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = usernameNormalized,
            Email = emailNormalized,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            Role = role,
            AllocatedQuotaBytes = targetQuota,
            UsedQuotaBytes = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        await _auditService.LogAsync(
            action: "UserCreated",
            resource: "Users",
            resourceId: user.Id.ToString(),
            userId: _currentUser.UserId,
            username: _currentUser.Username,
            ipAddress: ipAddress,
            details: new { createdUsername = user.Username, email = user.Email, role = user.Role.ToString(), quotaBytes = user.AllocatedQuotaBytes },
            ct: ct);

        return new AdminUserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role.ToString(),
            AllocatedQuotaBytes = user.AllocatedQuotaBytes,
            UsedQuotaBytes = user.UsedQuotaBytes,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            LastActiveAt = user.CreatedAt,
            ActiveSessionsCount = 0
        };
    }

    public async Task<AdminUserDto> UpdateUserAsync(Guid id, UpdateUserRequest request, string? ipAddress = null, CancellationToken ct = default)
    {
        var user = await _db.Users
            .Include(u => u.Sessions)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user == null)
        {
            throw new NotFoundException($"User with ID '{id}' was not found.");
        }

        // Self-protection: admin cannot deactivate or demote their own account
        if (user.Id == _currentUser.UserId)
        {
            if (request.IsActive.HasValue && !request.IsActive.Value)
            {
                throw new ValidationException("Administrator cannot deactivate their own account.");
            }

            if (!string.IsNullOrWhiteSpace(request.Role) &&
                Enum.TryParse<UserRole>(request.Role, true, out var targetRole) &&
                targetRole != UserRole.Admin)
            {
                throw new ValidationException("Administrator cannot remove their own admin privileges.");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
            {
                throw new ValidationException($"Invalid role '{request.Role}'. Supported roles: Admin, User.");
            }
            user.Role = role;
        }

        if (request.AllocatedQuotaBytes.HasValue)
        {
            if (request.AllocatedQuotaBytes.Value < 0)
            {
                throw new ValidationException("Allocated quota cannot be negative.");
            }

            var targetQuota = request.AllocatedQuotaBytes.Value;
            var totalActiveNodesCapacity = await _db.StorageNodes
                .Where(n => n.IsActive)
                .SumAsync(n => n.MaxCapacityBytes, ct);

            if (targetQuota > totalActiveNodesCapacity)
            {
                throw new ValidationException($"Cannot allocate {targetQuota} bytes. User quota ({targetQuota} bytes) would exceed active storage nodes capacity ({totalActiveNodesCapacity} bytes).");
            }

            user.AllocatedQuotaBytes = targetQuota;
            await _notifier.NotifyQuotaUpdatedAsync(user.Id, user.UsedQuotaBytes, user.AllocatedQuotaBytes, ct);
        }

        if (request.IsActive.HasValue)
        {
            user.IsActive = request.IsActive.Value;
            if (!user.IsActive)
            {
                // Invalidate all sessions if user deactivated
                var activeSessions = user.Sessions.Where(s => s.RevokedAt == null).ToList();
                foreach (var s in activeSessions)
                {
                    s.RevokedAt = DateTime.UtcNow;
                    _revocationCache?.Revoke(s.Id);
                }
                await _notifier.NotifyForceLogoutAsync(user.Id, "Account has been deactivated.", null, ct);
            }
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _auditService.LogAsync(
            action: "UserUpdated",
            resource: "Users",
            resourceId: user.Id.ToString(),
            userId: _currentUser.UserId,
            username: _currentUser.Username,
            ipAddress: ipAddress,
            details: new { role = user.Role.ToString(), allocatedQuotaBytes = user.AllocatedQuotaBytes, isActive = user.IsActive },
            ct: ct);

        var now = DateTime.UtcNow;
        return new AdminUserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role.ToString(),
            AllocatedQuotaBytes = user.AllocatedQuotaBytes,
            UsedQuotaBytes = user.UsedQuotaBytes,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            LastActiveAt = user.Sessions.OrderByDescending(s => s.LastActiveAt).Select(s => (DateTime?)s.LastActiveAt).FirstOrDefault() ?? user.UpdatedAt,
            ActiveSessionsCount = user.Sessions.Count(s => s.RevokedAt == null && s.ExpiresAt > now)
        };
    }

    public async Task ResetPasswordAsync(Guid id, AdminResetPasswordRequest request, string? ipAddress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
        {
            throw new ValidationException("Password must be at least 6 characters long.");
        }

        var user = await _db.Users
            .Include(u => u.Sessions)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user == null)
        {
            throw new NotFoundException($"User with ID '{id}' was not found.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        // Revoke all existing sessions for security
        foreach (var session in user.Sessions.Where(s => s.RevokedAt == null))
        {
            session.RevokedAt = DateTime.UtcNow;
            _revocationCache?.Revoke(session.Id);
        }

        await _db.SaveChangesAsync(ct);

        await _notifier.NotifyForceLogoutAsync(user.Id, "Password has been reset by administrator. Please log in again.", null, ct);

        await _auditService.LogAsync(
            action: "PasswordReset",
            resource: "Users",
            resourceId: user.Id.ToString(),
            userId: _currentUser.UserId,
            username: _currentUser.Username,
            ipAddress: ipAddress,
            details: new { targetUsername = user.Username },
            ct: ct);
    }

    public async Task RevokeUserSessionsAsync(Guid id, string? ipAddress = null, CancellationToken ct = default)
    {
        var user = await _db.Users
            .Include(u => u.Sessions)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user == null)
        {
            throw new NotFoundException($"User with ID '{id}' was not found.");
        }

        var activeSessions = user.Sessions.Where(s => s.RevokedAt == null).ToList();
        foreach (var session in activeSessions)
        {
            session.RevokedAt = DateTime.UtcNow;
            _revocationCache?.Revoke(session.Id);
        }

        await _db.SaveChangesAsync(ct);

        await _notifier.NotifyForceLogoutAsync(user.Id, "Sessions have been terminated by administrator.", null, ct);

        await _auditService.LogAsync(
            action: "SessionsRevoked",
            resource: "Users",
            resourceId: user.Id.ToString(),
            userId: _currentUser.UserId,
            username: _currentUser.Username,
            ipAddress: ipAddress,
            details: new { targetUsername = user.Username, revokedCount = activeSessions.Count },
            ct: ct);
    }

    public async Task RevokeUserSessionAsync(Guid userId, Guid sessionId, string? ipAddress = null, CancellationToken ct = default)
    {
        var user = await _db.Users
            .Include(u => u.Sessions)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user == null)
        {
            throw new NotFoundException($"User with ID '{userId}' was not found.");
        }

        var session = user.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session == null)
        {
            throw new NotFoundException($"Session with ID '{sessionId}' was not found.");
        }

        if (session.RevokedAt == null)
        {
            session.RevokedAt = DateTime.UtcNow;
            _revocationCache?.Revoke(sessionId);
            await _db.SaveChangesAsync(ct);

            await _notifier.NotifyForceLogoutAsync(user.Id, "Session has been terminated by administrator.", sessionId, ct);

            await _auditService.LogAsync(
                action: "SessionRevoked",
                resource: "Users",
                resourceId: sessionId.ToString(),
                userId: _currentUser.UserId,
                username: _currentUser.Username,
                ipAddress: ipAddress,
                details: new { targetUserId = userId, targetUsername = user.Username, sessionId, deviceName = session.DeviceName },
                ct: ct);
        }
    }

    public async Task DeleteUserAsync(Guid id, string? ipAddress = null, CancellationToken ct = default)
    {
        var user = await _db.Users
            .Include(u => u.Sessions)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user == null)
        {
            throw new NotFoundException($"User with ID '{id}' was not found.");
        }

        if (user.Id == _currentUser.UserId)
        {
            throw new ValidationException("Administrator cannot delete their own account.");
        }

        // Revoke sessions and notify force logout
        foreach (var s in user.Sessions.Where(s => s.RevokedAt == null))
        {
            s.RevokedAt = DateTime.UtcNow;
            _revocationCache?.Revoke(s.Id);
        }

        await _notifier.NotifyForceLogoutAsync(user.Id, "User account has been deleted.", null, ct);

        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);

        await _auditService.LogAsync(
            action: "UserDeleted",
            resource: "Users",
            resourceId: id.ToString(),
            userId: _currentUser.UserId,
            username: _currentUser.Username,
            ipAddress: ipAddress,
            details: new { deletedUsername = user.Username, email = user.Email },
            ct: ct);
    }
}
