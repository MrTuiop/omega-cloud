using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmegaCloud.Application.Services;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;
using OmegaCloud.Infrastructure.Data;
using OmegaCloud.Infrastructure.Storage;

namespace OmegaCloud.Tests.Services;

public class UploadGcServiceTests : IDisposable
{
    private readonly Guid _testUserId = Guid.NewGuid();
    private readonly string _tempStorageDirectory;
    private readonly LocalDiskStorageDriver _driver;
    private readonly StorageDriverResolver _resolver;
    private readonly StorageNode _localNode;

    public UploadGcServiceTests()
    {
        _tempStorageDirectory = Path.Combine(Path.GetTempPath(), "UploadGcTests_" + Guid.NewGuid().ToString("N"));
        _driver = new LocalDiskStorageDriver(_tempStorageDirectory, NullLogger<LocalDiskStorageDriver>.Instance);
        _resolver = new StorageDriverResolver(_driver);

        _localNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Local Storage",
            RegistrationKey = "local-key",
            NodeSecretHash = "secret",
            Type = StorageNodeType.LocalDisk,
            Status = StorageNodeStatus.Online,
            TotalSpaceBytes = 1_000_000_000L,
            AvailableSpaceBytes = 1_000_000_000L
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempStorageDirectory))
        {
            try
            {
                Directory.Delete(_tempStorageDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options);
        db.Database.EnsureCreated();

        var user = new User
        {
            Id = _testUserId,
            Username = "gcuser",
            Email = "gcuser@example.com",
            PasswordHash = "hash",
            Role = UserRole.User,
            AllocatedQuotaBytes = 100_000_000L,
            UsedQuotaBytes = 0L,
            IsActive = true
        };

        db.Users.Add(user);
        db.StorageNodes.Add(_localNode);
        db.SaveChanges();

        return db;
    }

    [Fact]
    public async Task CleanupExpiredSessions_ExpiresPastDueSessionsAndCleansTemp()
    {
        using var db = CreateDbContext();
        var gcService = new UploadGcService(db, _resolver);

        var expiredSession = new UploadSession
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            StorageNodeId = _localNode.Id,
            FileName = "expired.iso",
            TotalSizeBytes = 50_000_000,
            ChunkSizeBytes = 10_000_000,
            TotalChunks = 5,
            Status = UploadSessionStatus.Uploading,
            TempStoragePath = $"temp/sessions/{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            ExpiresAt = DateTime.UtcNow.AddHours(-1) // Already expired
        };

        var activeSession = new UploadSession
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            StorageNodeId = _localNode.Id,
            FileName = "active.iso",
            TotalSizeBytes = 50_000_000,
            ChunkSizeBytes = 10_000_000,
            TotalChunks = 5,
            Status = UploadSessionStatus.Uploading,
            TempStoragePath = $"temp/sessions/{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(12) // Still active
        };

        var completedSession = new UploadSession
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            StorageNodeId = _localNode.Id,
            FileName = "completed.iso",
            TotalSizeBytes = 50_000_000,
            ChunkSizeBytes = 10_000_000,
            TotalChunks = 5,
            Status = UploadSessionStatus.Completed,
            TempStoragePath = $"temp/sessions/{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow.AddDays(-5),
            ExpiresAt = DateTime.UtcNow.AddDays(-4) // Expired date, but Completed!
        };

        // Create dummy session temp directories
        var expiredDir = Path.Combine(_tempStorageDirectory, "temp", "sessions", expiredSession.Id.ToString("N"));
        Directory.CreateDirectory(expiredDir);
        File.WriteAllText(Path.Combine(expiredDir, "0.part"), "chunk0");

        db.UploadSessions.AddRange(expiredSession, activeSession, completedSession);
        await db.SaveChangesAsync();

        var result = await gcService.CleanupExpiredSessionsAsync();

        Assert.Equal(1, result.ExpiredSessionsCount);
        Assert.Equal(1, result.CleanedTempFoldersCount);

        var sessionInDb = await db.UploadSessions.FindAsync(expiredSession.Id);
        Assert.Equal(UploadSessionStatus.Expired, sessionInDb!.Status);
        Assert.False(Directory.Exists(expiredDir));

        var activeInDb = await db.UploadSessions.FindAsync(activeSession.Id);
        Assert.Equal(UploadSessionStatus.Uploading, activeInDb!.Status);

        var completedInDb = await db.UploadSessions.FindAsync(completedSession.Id);
        Assert.Equal(UploadSessionStatus.Completed, completedInDb!.Status);
    }

    [Fact]
    public async Task CleanupExpiredSessions_RemovesVeryOldDeadSessions()
    {
        using var db = CreateDbContext();
        var gcService = new UploadGcService(db, _resolver);

        var oldExpiredSession = new UploadSession
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            StorageNodeId = _localNode.Id,
            FileName = "ancient.zip",
            TotalSizeBytes = 10_000_000,
            ChunkSizeBytes = 5_000_000,
            TotalChunks = 2,
            Status = UploadSessionStatus.Expired,
            TempStoragePath = $"temp/sessions/{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow.AddDays(-20),
            ExpiresAt = DateTime.UtcNow.AddDays(-19) // Expired 19 days ago
        };

        db.UploadSessions.Add(oldExpiredSession);
        await db.SaveChangesAsync();

        var result = await gcService.CleanupExpiredSessionsAsync(removeSessionsOlderThan: TimeSpan.FromDays(7));

        Assert.Equal(1, result.RemovedOldSessionsCount);
        Assert.Null(await db.UploadSessions.FindAsync(oldExpiredSession.Id));
    }

    [Fact]
    public async Task CleanupExpiredSessions_AbortedSession_PreservesAbortedStatus()
    {
        using var db = CreateDbContext();
        var gcService = new UploadGcService(db, _resolver);

        var abortedSession = new UploadSession
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            StorageNodeId = _localNode.Id,
            FileName = "aborted.zip",
            TotalSizeBytes = 10_000_000,
            ChunkSizeBytes = 5_000_000,
            TotalChunks = 2,
            Status = UploadSessionStatus.Aborted,
            TempStoragePath = $"temp/sessions/{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            ExpiresAt = DateTime.UtcNow.AddHours(-1) // Passed expiration time
        };

        db.UploadSessions.Add(abortedSession);
        await db.SaveChangesAsync();

        var result = await gcService.CleanupExpiredSessionsAsync();

        Assert.Equal(0, result.ExpiredSessionsCount); // Should not count as newly expired

        var sessionInDb = await db.UploadSessions.FindAsync(abortedSession.Id);
        Assert.NotNull(sessionInDb);
        Assert.Equal(UploadSessionStatus.Aborted, sessionInDb.Status); // Preserves Aborted
    }
}
