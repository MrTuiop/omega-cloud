using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Infrastructure.Data.Seeding;

public class DatabaseSeeder
{
    public static readonly Guid MasterLocalStorageNodeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly AppDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseSeeder> _logger;
    private readonly IDiskTelemetryProvider? _diskTelemetryProvider;

    public DatabaseSeeder(
        AppDbContext dbContext,
        IPasswordHasher passwordHasher,
        IConfiguration configuration,
        ILogger<DatabaseSeeder> logger,
        IDiskTelemetryProvider? diskTelemetryProvider = null)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
        _logger = logger;
        _diskTelemetryProvider = diskTelemetryProvider;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedAdminAsync(cancellationToken);
        await SeedLocalStorageNodeAsync(cancellationToken);
    }

    private async Task SeedAdminAsync(CancellationToken cancellationToken)
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

    private async Task SeedLocalStorageNodeAsync(CancellationToken cancellationToken)
    {
        var localNode = await _dbContext.StorageNodes.FirstOrDefaultAsync(n => n.Type == StorageNodeType.LocalDisk, cancellationToken);
        var diskInfo = _diskTelemetryProvider?.GetMasterDiskSpace()
            ?? new DiskSpaceInfo(100L * 1024 * 1024 * 1024, 80L * 1024 * 1024 * 1024);

        if (localNode != null)
        {
            // If existing local node still has the legacy hardcoded 10 TB default, update it to physical telemetry
            if (localNode.MaxCapacityBytes == 10_995_116_277_760L && diskInfo.TotalBytes > 0)
            {
                localNode.MaxCapacityBytes = diskInfo.TotalBytes;
                localNode.TotalSpaceBytes = diskInfo.TotalBytes;
                localNode.AvailableSpaceBytes = diskInfo.AvailableFreeBytes;
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Migrated legacy Master Local Storage node capacity to physical disk size: {TotalBytes} bytes", diskInfo.TotalBytes);
            }
            return;
        }

        _logger.LogInformation("No local storage node found. Seeding default Master Local Storage node.");

        localNode = new StorageNode
        {
            Id = MasterLocalStorageNodeId,
            Name = "Master Local Storage",
            RegistrationKey = "master-local-storage-daemon-key",
            NodeSecretHash = "master_local_internal_secret",
            Type = StorageNodeType.LocalDisk,
            BaseUrl = null,
            IsBehindNat = false,
            Status = StorageNodeStatus.Online,
            TotalSpaceBytes = diskInfo.TotalBytes,
            AvailableSpaceBytes = diskInfo.AvailableFreeBytes,
            MaxCapacityBytes = diskInfo.TotalBytes,
            LastHeartbeatAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.StorageNodes.Add(localNode);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Successfully seeded Master Local Storage node: {NodeId}", localNode.Id);
    }
}
