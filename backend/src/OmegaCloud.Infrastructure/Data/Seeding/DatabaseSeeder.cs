using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Infrastructure.Data.Seeding;

public class DatabaseSeeder
{
    private readonly AppDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        AppDbContext dbContext,
        IPasswordHasher passwordHasher,
        IConfiguration configuration,
        ILogger<DatabaseSeeder> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var hasAdmin = await _dbContext.Users.AnyAsync(u => u.Role == UserRole.Admin, cancellationToken);
        if (hasAdmin)
        {
            return;
        }

        var username = _configuration["AdminSeed:Username"] ?? "admin";
        var email = _configuration["AdminSeed:Email"] ?? "admin@omegacloud.local";
        var password = _configuration["AdminSeed:Password"] ?? "AdminPassword123!";
        var quotaStr = _configuration["AdminSeed:AllocatedQuotaBytes"];
        var allocatedQuota = long.TryParse(quotaStr, out var q) ? q : 1_099_511_627_776L; // 1 TB default

        _logger.LogInformation("No administrator account found. Seeding initial admin user: {Username} ({Email})", username, email);

        var adminUser = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            Email = email,
            PasswordHash = _passwordHasher.HashPassword(password),
            Role = UserRole.Admin,
            AllocatedQuotaBytes = allocatedQuota,
            UsedQuotaBytes = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(adminUser);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Successfully created initial administrator account: {Username}", username);
    }
}
