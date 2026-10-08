using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;
using OmegaCloud.Infrastructure.Data;
using OmegaCloud.Infrastructure.Data.Seeding;

namespace OmegaCloud.Tests.Integration;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly string _testStoragePath = Path.Combine(Path.GetTempPath(), "OmegaCloudTests_" + Guid.NewGuid().ToString("N"));

    public const string DefaultAdminUsername = "admin";
    public const string DefaultAdminPassword = "AdminPassword123!";
    public const string DefaultAdminEmail = "admin@omegacloud.local";

    public const string DefaultRegularUsername = "standarduser";
    public const string DefaultRegularPassword = "UserPass123!";
    public const string DefaultRegularEmail = "standard@omegacloud.local";

    public string TestStoragePath => _testStoragePath;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("UseInMemoryDatabase", "true");
        builder.UseSetting("InMemoryDatabaseName", _databaseName);
        builder.UseSetting("Storage:LocalRootPath", _testStoragePath);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        db.Database.EnsureCreated();

        if (!db.StorageNodes.Any(n => n.Type == StorageNodeType.LocalDisk))
        {
            var localNode = new StorageNode
            {
                Id = DatabaseSeeder.MasterLocalStorageNodeId,
                Name = "Master Local Storage",
                RegistrationKey = "test-local-storage-key",
                NodeSecretHash = "test_local_secret",
                Type = StorageNodeType.LocalDisk,
                BaseUrl = null,
                IsBehindNat = false,
                Status = StorageNodeStatus.Online,
                TotalSpaceBytes = 1_099_511_627_776L,
                AvailableSpaceBytes = 1_099_511_627_776L,
                LastHeartbeatAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            db.StorageNodes.Add(localNode);
        }

        if (!db.Users.Any(u => u.Username == DefaultAdminUsername))
        {
            var admin = new User
            {
                Id = Guid.NewGuid(),
                Username = DefaultAdminUsername,
                Email = DefaultAdminEmail,
                PasswordHash = passwordHasher.HashPassword(DefaultAdminPassword),
                Role = UserRole.Admin,
                AllocatedQuotaBytes = 1_099_511_627_776L,
                UsedQuotaBytes = 0,
                IsActive = true
            };
            db.Users.Add(admin);
        }

        if (!db.Users.Any(u => u.Username == DefaultRegularUsername))
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = DefaultRegularUsername,
                Email = DefaultRegularEmail,
                PasswordHash = passwordHasher.HashPassword(DefaultRegularPassword),
                Role = UserRole.User,
                AllocatedQuotaBytes = 107_374_182_400L,
                UsedQuotaBytes = 5_242_880L,
                IsActive = true
            };
            db.Users.Add(user);
        }

        db.SaveChanges();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(_testStoragePath))
        {
            try
            {
                Directory.Delete(_testStoragePath, recursive: true);
            }
            catch
            {
                // Best-effort temp dir cleanup
            }
        }
    }
}
