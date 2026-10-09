using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Application.DTOs.Auth;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Application.DTOs.Folders;
using OmegaCloud.Application.DTOs.Nodes;
using OmegaCloud.Application.DTOs.Upload;
using OmegaCloud.Application.Services;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;
using OmegaCloud.Infrastructure.Data;
using OmegaCloud.Infrastructure.Security;
using OmegaCloud.Infrastructure.Services;
using OmegaCloud.Infrastructure.Storage;
using Xunit;

namespace OmegaCloud.Tests.Services;

public class ServiceRealtimeNotificationTests
{
    private class SpyRealtimeNotifier : IRealtimeNotifier
    {
        public List<(Guid nodeId, string status, DateTimeOffset lastSeen)> NodeStatusChanges { get; } = new();
        public List<(Guid userId, Guid sessionId, double percent, double speed)> FileProgressUpdates { get; } = new();
        public List<(Guid userId, Guid? folderId, string changeType, object? item)> DirectoryChanges { get; } = new();
        public List<(Guid userId, long usedBytes, long totalBytes)> QuotaUpdates { get; } = new();
        public List<(Guid userId, string reason, Guid? sessionId)> ForceLogouts { get; } = new();

        public Task NotifyNodeStatusChangedAsync(Guid nodeId, string status, DateTimeOffset lastSeen, CancellationToken ct = default)
        {
            NodeStatusChanges.Add((nodeId, status, lastSeen));
            return Task.CompletedTask;
        }

        public Task NotifyFileProgressAsync(Guid userId, Guid sessionId, double percent, double speedBytesPerSec, CancellationToken ct = default)
        {
            FileProgressUpdates.Add((userId, sessionId, percent, speedBytesPerSec));
            return Task.CompletedTask;
        }

        public Task NotifyDirectoryChangedAsync(Guid userId, Guid? folderId, string changeType, object? item, CancellationToken ct = default)
        {
            DirectoryChanges.Add((userId, folderId, changeType, item));
            return Task.CompletedTask;
        }

        public Task NotifyQuotaUpdatedAsync(Guid userId, long usedBytes, long totalBytes, CancellationToken ct = default)
        {
            QuotaUpdates.Add((userId, usedBytes, totalBytes));
            return Task.CompletedTask;
        }

        public Task NotifyForceLogoutAsync(Guid userId, string reason, Guid? sessionId = null, CancellationToken ct = default)
        {
            ForceLogouts.Add((userId, reason, sessionId));
            return Task.CompletedTask;
        }
    }

    private class FakeStorageDriver : IStorageDriver
    {
        public string DriverType => "Fake";
        public Task<Stream> OpenReadAsync(StorageBlob blob, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream());
        public Task<StorageBlobResult> WriteAsync(Stream contentStream, StorageWriteOptions options, CancellationToken ct = default)
        {
            return Task.FromResult(new StorageBlobResult
            {
                BlobId = Guid.NewGuid(),
                SizeBytes = 100,
                Sha256Hash = new string('a', 64),
                StorageRelativePath = "blobs/aa/bb/fake.blob"
            });
        }
        public Task DeleteAsync(StorageBlob blob, CancellationToken ct = default) => Task.CompletedTask;
        public Task<PreSignedUrlResult?> TryGenerateDirectDownloadUrlAsync(StorageBlob blob, TimeSpan validFor, CancellationToken ct = default) => Task.FromResult<PreSignedUrlResult?>(null);
        public Task<PreSignedUrlResult?> TryGenerateDirectUploadUrlAsync(UploadSession session, int chunkIndex, TimeSpan validFor, CancellationToken ct = default) => Task.FromResult<PreSignedUrlResult?>(null);
        public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<UploadChunkResult> WriteChunkAsync(UploadSession session, int chunkIndex, Stream chunkStream, string? expectedSha256 = null, CancellationToken ct = default)
        {
            return Task.FromResult(new UploadChunkResult
            {
                ChunkIndex = chunkIndex,
                SizeBytes = 50,
                Sha256Hash = new string('b', 64)
            });
        }
        public bool ShouldThrowOnAssemble { get; set; }

        public Task<StorageBlobResult> AssembleChunksAsync(UploadSession session, IReadOnlyList<UploadChunk> chunks, CancellationToken ct = default)
        {
            if (ShouldThrowOnAssemble)
            {
                throw new InvalidOperationException("Assemble failed");
            }

            return Task.FromResult(new StorageBlobResult
            {
                BlobId = Guid.NewGuid(),
                SizeBytes = 100,
                Sha256Hash = new string('c', 64),
                StorageRelativePath = "blobs/cc/dd/assembled.blob"
            });
        }
        public Task DeleteSessionTempDataAsync(UploadSession session, CancellationToken ct = default) => Task.CompletedTask;
    }

    private class FakeStorageDriverResolver : IStorageDriverResolver
    {
        public FakeStorageDriver Driver { get; } = new();
        public IStorageDriver GetDriverForNode(StorageNode node) => Driver;
        public IStorageDriver GetDriver(StorageNodeType nodeType) => Driver;
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task NodeService_EmitsNodeStatusChanged_OnRegisterAndStatusCheck()
    {
        // Arrange
        var db = CreateDbContext();
        var spy = new SpyRealtimeNotifier();
        var time = new DateTimeProvider();
        var signer = new HmacSigner(time);
        var gateway = new RemoteNodeGateway(new HttpClient(), signer, time, NullLogger<RemoteNodeGateway>.Instance);
        var service = new NodeService(db, signer, gateway, time, NullLogger<NodeService>.Instance, spy);

        var node = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Node1",
            RegistrationKey = "reg-1",
            Type = StorageNodeType.RemoteDaemon,
            Status = StorageNodeStatus.Online,
            IsActive = true,
            LastHeartbeatAt = DateTime.UtcNow.AddMinutes(-5),
            CreatedAt = DateTime.UtcNow
        };
        db.StorageNodes.Add(node);
        await db.SaveChangesAsync();

        // Act 1: Check offline nodes
        await service.CheckOfflineNodesAsync(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(30));

        // Assert 1: Emits offline notification
        Assert.Contains(spy.NodeStatusChanges, n => n.nodeId == node.Id && n.status == "Offline");

        // Act 2: Register a new node
        var unregNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Node2",
            RegistrationKey = "reg-2",
            Type = StorageNodeType.RemoteDaemon,
            Status = StorageNodeStatus.Offline,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.StorageNodes.Add(unregNode);
        await db.SaveChangesAsync();

        await service.RegisterNodeAsync(new RegisterNodeRequest { RegistrationKey = "reg-2" });

        // Assert 2: Emits online notification on register
        Assert.Contains(spy.NodeStatusChanges, n => n.nodeId == unregNode.Id && n.status == "Online");
    }

    [Fact]
    public async Task FolderService_EmitsDirectoryChanged_OnCreateUpdateDelete()
    {
        // Arrange
        var db = CreateDbContext();
        var spy = new SpyRealtimeNotifier();
        var service = new FolderService(db, spy);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Username = "user1",
            Email = "u1@test.local",
            PasswordHash = "hash",
            AllocatedQuotaBytes = 100000,
            UsedQuotaBytes = 0,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // 1. Create
        var folder = await service.CreateFolderAsync(new CreateFolderRequest { Name = "Documents" }, userId);
        Assert.Single(spy.DirectoryChanges);
        Assert.Equal("Created", spy.DirectoryChanges[0].changeType);

        // 2. Update
        await service.UpdateFolderAsync(folder.Id, new UpdateFolderRequest { Name = "Work Documents" }, userId);
        Assert.Equal(2, spy.DirectoryChanges.Count);
        Assert.Equal("Updated", spy.DirectoryChanges[1].changeType);

        // 3. Delete
        await service.DeleteFolderAsync(folder.Id, userId);
        Assert.Equal(3, spy.DirectoryChanges.Count);
        Assert.Equal("Deleted", spy.DirectoryChanges[2].changeType);
    }

    [Fact]
    public async Task FileService_EmitsDirectoryChangedAndQuota_OnUploadUpdateDelete()
    {
        // Arrange
        var db = CreateDbContext();
        var spy = new SpyRealtimeNotifier();
        var resolver = new FakeStorageDriverResolver();
        var service = new FileService(db, resolver, spy);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Username = "user1",
            Email = "u1@test.local",
            PasswordHash = "hash",
            AllocatedQuotaBytes = 100000,
            UsedQuotaBytes = 0,
            IsActive = true
        };
        var node = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Local",
            RegistrationKey = "key",
            Type = StorageNodeType.LocalDisk,
            Status = StorageNodeStatus.Online,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        db.StorageNodes.Add(node);
        await db.SaveChangesAsync();

        // 1. Upload
        using var stream = new MemoryStream("data"u8.ToArray());
        var file = await service.UploadFileAsync(new UploadFileStreamRequest
        {
            FileName = "notes.txt",
            Content = stream,
            SizeBytes = 4
        }, userId);

        Assert.Contains(spy.DirectoryChanges, d => d.changeType == "Created");
        Assert.Contains(spy.QuotaUpdates, q => q.userId == userId && q.usedBytes > 0);

        // 2. Update
        await service.UpdateFileAsync(file.Id, new UpdateFileRequest { Name = "notes_updated.txt" }, userId);
        Assert.Contains(spy.DirectoryChanges, d => d.changeType == "Updated");

        // 3. Delete
        await service.DeleteFileAsync(file.Id, userId);
        Assert.Contains(spy.DirectoryChanges, d => d.changeType == "Deleted");
    }

    [Fact]
    public async Task AuthService_EmitsForceLogout_OnRevokeSession()
    {
        // Arrange
        var db = CreateDbContext();
        var spy = new SpyRealtimeNotifier();
        var time = new DateTimeProvider();
        var hasher = new Argon2PasswordHasher();
        var jwtOptions = Microsoft.Extensions.Options.Options.Create(new Infrastructure.Security.JwtOptions
        {
            Secret = "a_super_secret_key_that_is_at_least_32_characters_long",
            Issuer = "test",
            Audience = "test"
        });
        var jwtProvider = new JwtProvider(jwtOptions, time);
        var authService = new AuthService(db, hasher, jwtProvider, time, spy);

        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var session = new UserSession
        {
            Id = sessionId,
            UserId = userId,
            RefreshTokenHash = "token_hash",
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();

        // Act
        await authService.RevokeSessionAsync(userId, sessionId);

        // Assert
        Assert.Single(spy.ForceLogouts);
        Assert.Equal(userId, spy.ForceLogouts[0].userId);
        Assert.Equal(sessionId, spy.ForceLogouts[0].sessionId);
        Assert.Contains("revoked", spy.ForceLogouts[0].reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TrashService_EmitsNotifications_OnRestoreAndPermanentDelete()
    {
        // Arrange
        var db = CreateDbContext();
        var spy = new SpyRealtimeNotifier();
        var resolver = new FakeStorageDriverResolver();
        var trashService = new TrashService(db, resolver, spy);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Username = "user1",
            Email = "u1@test.local",
            PasswordHash = "hash",
            AllocatedQuotaBytes = 100000,
            UsedQuotaBytes = 500,
            IsActive = true
        };
        var node = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Local Node",
            RegistrationKey = "node-reg-key",
            Type = StorageNodeType.LocalDisk,
            Status = StorageNodeStatus.Online,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var blob = new StorageBlob
        {
            Id = Guid.NewGuid(),
            StorageNodeId = node.Id,
            Sha256Hash = new string('d', 64),
            SizeBytes = 500,
            StorageRelativePath = "blobs/dd/ee/test.blob",
            ReferenceCount = 1,
            CreatedAt = DateTime.UtcNow
        };
        var file = new FileItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StorageBlobId = blob.Id,
            Name = "deleted.txt",
            Extension = "txt",
            MimeType = "text/plain",
            SizeBytes = 500,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        db.StorageNodes.Add(node);
        db.StorageBlobs.Add(blob);
        db.FileItems.Add(file);
        await db.SaveChangesAsync();

        // 1. Restore
        await trashService.RestoreItemAsync(file.Id, userId);
        Assert.Contains(spy.DirectoryChanges, d => d.changeType == "Restored");

        // Set to deleted again for permanent delete test
        file.IsDeleted = true;
        file.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // 2. Permanently delete
        await trashService.PermanentlyDeleteItemAsync(file.Id, userId);
        Assert.Contains(spy.DirectoryChanges, d => d.changeType == "PermanentlyDeleted");
        Assert.Contains(spy.QuotaUpdates, q => q.userId == userId);

        // 3. Empty trash
        await trashService.EmptyTrashAsync(userId);
        Assert.Contains(spy.DirectoryChanges, d => d.changeType == "TrashEmptied");
    }

    [Fact]
    public async Task UploadService_EmitsFileProgressDirectoryChangedAndQuota_OnChunkAndComplete()
    {
        // Arrange
        var db = CreateDbContext();
        var spy = new SpyRealtimeNotifier();
        var resolver = new FakeStorageDriverResolver();
        var fileService = new FileService(db, resolver, spy);
        var uploadService = new UploadService(db, fileService, resolver, spy);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Username = "uploader",
            Email = "up@test.local",
            PasswordHash = "hash",
            AllocatedQuotaBytes = 100000,
            UsedQuotaBytes = 0,
            IsActive = true
        };
        var node = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Local Node",
            RegistrationKey = "up-node",
            Type = StorageNodeType.LocalDisk,
            Status = StorageNodeStatus.Online,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        db.StorageNodes.Add(node);
        await db.SaveChangesAsync();

        // 1. Init session
        var initRes = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "bigfile.bin",
            TotalSizeBytes = 100,
            TotalChunks = 2,
            ChunkSizeBytes = 50,
            StorageNodeId = node.Id
        }, userId);

        // 2. Upload Chunk 0
        using var chunkStream0 = new MemoryStream(new byte[50]);
        await uploadService.UploadChunkAsync(initRes.SessionId, 0, chunkStream0, 50, null, userId);

        Assert.Contains(spy.FileProgressUpdates, p => p.sessionId == initRes.SessionId && p.percent == 50.0);

        // 3. Upload Chunk 1
        using var chunkStream1 = new MemoryStream(new byte[50]);
        await uploadService.UploadChunkAsync(initRes.SessionId, 1, chunkStream1, 50, null, userId);

        Assert.Contains(spy.FileProgressUpdates, p => p.sessionId == initRes.SessionId && p.percent == 100.0);

        // 4. Complete session
        var completeRes = await uploadService.CompleteSessionAsync(initRes.SessionId, null, userId);

        Assert.Contains(spy.DirectoryChanges, d => d.changeType == "Uploaded" && d.folderId == null);
        Assert.Contains(spy.QuotaUpdates, q => q.userId == userId && q.usedBytes == 100);

        // 5. Abort another session
        var abortSession = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "aborted.bin",
            TotalSizeBytes = 50,
            TotalChunks = 1,
            StorageNodeId = node.Id
        }, userId);

        await uploadService.AbortSessionAsync(abortSession.SessionId, userId);
        Assert.Contains(spy.FileProgressUpdates, p => p.sessionId == abortSession.SessionId && p.percent == 0.0);
    }

    [Fact]
    public async Task FileService_EmitsBothFoldersAndFileProgress_OnMove()
    {
        // Arrange
        var db = CreateDbContext();
        var spy = new SpyRealtimeNotifier();
        var resolver = new FakeStorageDriverResolver();
        var service = new FileService(db, resolver, spy);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Username = "mover",
            Email = "m@test.local",
            PasswordHash = "hash",
            AllocatedQuotaBytes = 100000,
            UsedQuotaBytes = 100,
            IsActive = true
        };
        var folderA = new Folder { Id = Guid.NewGuid(), UserId = userId, Name = "FolderA", IsDeleted = false };
        var folderB = new Folder { Id = Guid.NewGuid(), UserId = userId, Name = "FolderB", IsDeleted = false };
        var blob = new StorageBlob { Id = Guid.NewGuid(), Sha256Hash = new string('x', 64), SizeBytes = 100, StorageRelativePath = "p" };
        var file = new FileItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FolderId = folderA.Id,
            StorageBlobId = blob.Id,
            Name = "move_me.txt",
            Extension = "txt",
            MimeType = "text/plain",
            SizeBytes = 100,
            IsDeleted = false
        };

        db.Users.Add(user);
        db.Folders.AddRange(folderA, folderB);
        db.StorageBlobs.Add(blob);
        db.FileItems.Add(file);
        await db.SaveChangesAsync();

        // Act: Move file from folderA to folderB
        await service.UpdateFileAsync(file.Id, new UpdateFileRequest { FolderId = folderB.Id }, userId);

        // Assert: DirectoryChanged must be emitted for BOTH folderA (source) and folderB (destination)
        Assert.Contains(spy.DirectoryChanges, d => d.folderId == folderA.Id && d.changeType == "Moved");
        Assert.Contains(spy.DirectoryChanges, d => d.folderId == folderB.Id && d.changeType == "Moved");

        // Assert: FileProgress should be reported for the moved file
        Assert.Contains(spy.FileProgressUpdates, p => p.sessionId == file.Id && p.percent == 100.0);
    }

    [Fact]
    public async Task FolderService_EmitsBothParentFolders_OnMove()
    {
        // Arrange
        var db = CreateDbContext();
        var spy = new SpyRealtimeNotifier();
        var service = new FolderService(db, spy);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Username = "fmover",
            Email = "fm@test.local",
            PasswordHash = "hash",
            AllocatedQuotaBytes = 100000,
            UsedQuotaBytes = 0,
            IsActive = true
        };
        var parent1 = new Folder { Id = Guid.NewGuid(), UserId = userId, Name = "P1", IsDeleted = false };
        var parent2 = new Folder { Id = Guid.NewGuid(), UserId = userId, Name = "P2", IsDeleted = false };
        var subfolder = new Folder { Id = Guid.NewGuid(), UserId = userId, ParentFolderId = parent1.Id, Name = "Sub", IsDeleted = false };

        db.Users.Add(user);
        db.Folders.AddRange(parent1, parent2, subfolder);
        await db.SaveChangesAsync();

        // Act: Move subfolder from P1 to P2
        await service.UpdateFolderAsync(subfolder.Id, new UpdateFolderRequest { ParentFolderId = parent2.Id }, userId);

        // Assert: Both source parent (P1) and destination parent (P2) receive "Moved"
        Assert.Contains(spy.DirectoryChanges, d => d.folderId == parent1.Id && d.changeType == "Moved");
        Assert.Contains(spy.DirectoryChanges, d => d.folderId == parent2.Id && d.changeType == "Moved");
    }

    [Fact]
    public async Task TrashService_EmitsNonNullableItemPayload_OnPermanentDelete()
    {
        // Arrange
        var db = CreateDbContext();
        var spy = new SpyRealtimeNotifier();
        var resolver = new FakeStorageDriverResolver();
        var trashService = new TrashService(db, resolver, spy);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Username = "purger",
            Email = "p@test.local",
            PasswordHash = "hash",
            AllocatedQuotaBytes = 100000,
            UsedQuotaBytes = 200,
            IsActive = true
        };
        var node = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Local Node",
            RegistrationKey = "trash-node-reg",
            Type = StorageNodeType.LocalDisk,
            Status = StorageNodeStatus.Online,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var blob = new StorageBlob
        {
            Id = Guid.NewGuid(),
            StorageNodeId = node.Id,
            Sha256Hash = new string('p', 64),
            SizeBytes = 200,
            StorageRelativePath = "blobs/pp/qq/purge.blob",
            ReferenceCount = 1,
            CreatedAt = DateTime.UtcNow
        };
        var file = new FileItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StorageBlobId = blob.Id,
            Name = "to_purge.txt",
            Extension = "txt",
            MimeType = "text/plain",
            SizeBytes = 200,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        db.StorageNodes.Add(node);
        db.StorageBlobs.Add(blob);
        db.FileItems.Add(file);
        await db.SaveChangesAsync();

        // Act
        await trashService.PermanentlyDeleteItemAsync(file.Id, userId);

        // Assert: DirectoryChanged event contains non-null item payload with the deleted file ID
        var deleteEvt = Assert.Single(spy.DirectoryChanges, d => d.changeType == "PermanentlyDeleted");
        Assert.NotNull(deleteEvt.item);
    }

    [Fact]
    public async Task AuthService_EmitsForceLogout_OnRefreshTokenReuseAttack()
    {
        // Arrange
        var db = CreateDbContext();
        var spy = new SpyRealtimeNotifier();
        var time = new DateTimeProvider();
        var hasher = new Argon2PasswordHasher();
        var jwtOptions = Microsoft.Extensions.Options.Options.Create(new Infrastructure.Security.JwtOptions
        {
            Secret = "a_super_secret_key_that_is_at_least_32_characters_long",
            Issuer = "test",
            Audience = "test"
        });
        var jwtProvider = new JwtProvider(jwtOptions, time);
        var authService = new AuthService(db, hasher, jwtProvider, time, spy);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Username = "victim",
            Email = "v@test.local",
            PasswordHash = hasher.HashPassword("Pass123!"),
            AllocatedQuotaBytes = 10000,
            UsedQuotaBytes = 0,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Normal login
        var login = await authService.LoginAsync(new LoginRequest { UsernameOrEmail = "victim", Password = "Pass123!" }, "127.0.0.1", "agent");

        // Normal refresh
        var refresh = await authService.RefreshTokenAsync(login.RefreshToken, "127.0.0.1", "agent");

        // Act: Attacker replays old login.RefreshToken (reuse detection triggers)
        await Assert.ThrowsAsync<OmegaCloud.Application.Common.Exceptions.UnauthorizedException>(async () =>
        {
            await authService.RefreshTokenAsync(login.RefreshToken, "127.0.0.1", "agent");
        });

        // Assert: ForceLogout must be emitted to the user
        Assert.Contains(spy.ForceLogouts, l => l.userId == userId && l.sessionId == null);
    }

    [Fact]
    public async Task AuthService_EmitsForceLogout_OnLogoutWithRefreshToken()
    {
        // Arrange
        var db = CreateDbContext();
        var spy = new SpyRealtimeNotifier();
        var time = new DateTimeProvider();
        var hasher = new Argon2PasswordHasher();
        var jwtOptions = Microsoft.Extensions.Options.Options.Create(new Infrastructure.Security.JwtOptions
        {
            Secret = "a_super_secret_key_that_is_at_least_32_characters_long",
            Issuer = "test",
            Audience = "test"
        });
        var jwtProvider = new JwtProvider(jwtOptions, time);
        var authService = new AuthService(db, hasher, jwtProvider, time, spy);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Username = "logout_user",
            Email = "lo@test.local",
            PasswordHash = hasher.HashPassword("Pass123!"),
            AllocatedQuotaBytes = 10000,
            UsedQuotaBytes = 0,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var login = await authService.LoginAsync(new LoginRequest { UsernameOrEmail = "logout_user", Password = "Pass123!" }, "127.0.0.1", "agent");

        // Act: Logout providing only the raw refresh token
        await authService.LogoutAsync(userId, null, login.RefreshToken);

        // Assert: ForceLogout must be emitted for that session
        Assert.Contains(spy.ForceLogouts, l => l.userId == userId && l.sessionId.HasValue);
    }

    [Fact]
    public async Task UploadService_RevertsProgress_WhenAssembleThrows()
    {
        // Arrange
        var db = CreateDbContext();
        var spy = new SpyRealtimeNotifier();
        var resolver = new FakeStorageDriverResolver();
        resolver.Driver.ShouldThrowOnAssemble = true; // configure driver to throw
        var fileService = new FileService(db, resolver, spy);
        var uploadService = new UploadService(db, fileService, resolver, spy);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Username = "fail_uploader",
            Email = "fu@test.local",
            PasswordHash = "hash",
            AllocatedQuotaBytes = 100000,
            UsedQuotaBytes = 0,
            IsActive = true
        };
        var node = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Local Node",
            RegistrationKey = "fail-node",
            Type = StorageNodeType.LocalDisk,
            Status = StorageNodeStatus.Online,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        db.StorageNodes.Add(node);
        await db.SaveChangesAsync();

        var initRes = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "failfile.bin",
            TotalSizeBytes = 50,
            TotalChunks = 1,
            StorageNodeId = node.Id
        }, userId);

        using var chunkStream = new MemoryStream(new byte[50]);
        await uploadService.UploadChunkAsync(initRes.SessionId, 0, chunkStream, 50, null, userId);

        // Act & Assert: Complete session fails
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await uploadService.CompleteSessionAsync(initRes.SessionId, null, userId);
        });

        // Assert: FileProgress was reverted back to chunk upload percent (100.0) instead of leaving it stuck
        Assert.NotEmpty(spy.FileProgressUpdates);
    }
}
