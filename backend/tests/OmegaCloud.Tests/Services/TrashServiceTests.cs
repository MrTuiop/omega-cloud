using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Services;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;
using OmegaCloud.Infrastructure.Data;
using OmegaCloud.Infrastructure.Storage;

namespace OmegaCloud.Tests.Services;

public class TrashServiceTests : IDisposable
{
    private readonly Guid _testUserId = Guid.NewGuid();
    private readonly string _tempStorageDirectory;
    private readonly LocalDiskStorageDriver _driver;
    private readonly StorageDriverResolver _resolver;
    private readonly StorageNode _localNode;

    public TrashServiceTests()
    {
        _tempStorageDirectory = Path.Combine(Path.GetTempPath(), "TrashServiceTests_" + Guid.NewGuid().ToString("N"));
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
            Username = "trashuser",
            Email = "trashuser@example.com",
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

    private async Task<FileItem> SeedFileAsync(AppDbContext db, Guid userId, Guid? folderId, string name, long size, bool isDeleted = false, DateTime? deletedAt = null, int blobRefCount = 1)
    {
        var blob = new StorageBlob
        {
            Id = Guid.NewGuid(),
            StorageNodeId = _localNode.Id,
            Sha256Hash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            SizeBytes = size,
            StorageRelativePath = $"blobs/ab/cd/{Guid.NewGuid():N}.blob",
            ReferenceCount = blobRefCount,
            CreatedAt = DateTime.UtcNow
        };

        db.StorageBlobs.Add(blob);

        var file = new FileItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FolderId = folderId,
            StorageBlobId = blob.Id,
            Name = name,
            Extension = Path.GetExtension(name).TrimStart('.'),
            MimeType = "text/plain",
            SizeBytes = size,
            IsDeleted = isDeleted,
            DeletedAt = deletedAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.FileItems.Add(file);

        var user = await db.Users.FindAsync(userId);
        if (user != null)
        {
            user.UsedQuotaBytes += size;
        }

        await db.SaveChangesAsync();
        return file;
    }

    private async Task<Folder> SeedFolderAsync(AppDbContext db, Guid userId, Guid? parentId, string name, bool isDeleted = false, DateTime? deletedAt = null)
    {
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ParentFolderId = parentId,
            Name = name,
            IsDeleted = isDeleted,
            DeletedAt = deletedAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder;
    }

    [Fact]
    public async Task GetTrashContent_EmptyTrash_ReturnsEmptyCollections()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        var result = await service.GetTrashContentAsync(_testUserId);

        Assert.NotNull(result);
        Assert.Empty(result.Files);
        Assert.Empty(result.Folders);
        Assert.Equal(0, result.TotalTrashSizeBytes);
        Assert.Equal(0, result.TotalItemCount);
    }

    [Fact]
    public async Task GetTrashContent_WithDeletedItems_ReturnsAllDeletedItems()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        var folder = await SeedFolderAsync(db, _testUserId, null, "DeletedFolder", isDeleted: true, deletedAt: DateTime.UtcNow);
        var file = await SeedFileAsync(db, _testUserId, folder.Id, "deleted.txt", 1024, isDeleted: true, deletedAt: DateTime.UtcNow);

        var result = await service.GetTrashContentAsync(_testUserId, topLevelOnly: false);

        Assert.Single(result.Folders);
        Assert.Single(result.Files);
        Assert.Equal("DeletedFolder", result.Folders[0].Name);
        Assert.Equal("deleted.txt", result.Files[0].Name);
        Assert.Equal(1024, result.TotalTrashSizeBytes);
        Assert.Equal(2, result.TotalItemCount);
    }

    [Fact]
    public async Task GetTrashContent_TopLevelOnly_FiltersChildrenOfDeletedFolder()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        var parentFolder = await SeedFolderAsync(db, _testUserId, null, "ParentFolder", isDeleted: true, deletedAt: DateTime.UtcNow);
        var childFolder = await SeedFolderAsync(db, _testUserId, parentFolder.Id, "ChildFolder", isDeleted: true, deletedAt: DateTime.UtcNow);
        var fileInsideParent = await SeedFileAsync(db, _testUserId, parentFolder.Id, "file_inside.txt", 500, isDeleted: true, deletedAt: DateTime.UtcNow);
        var standaloneFile = await SeedFileAsync(db, _testUserId, null, "standalone.txt", 300, isDeleted: true, deletedAt: DateTime.UtcNow);

        var result = await service.GetTrashContentAsync(_testUserId, topLevelOnly: true);

        // Only top-level items: ParentFolder and standaloneFile
        Assert.Single(result.Folders);
        Assert.Equal("ParentFolder", result.Folders[0].Name);
        Assert.Single(result.Files);
        Assert.Equal("standalone.txt", result.Files[0].Name);
    }

    [Fact]
    public async Task RestoreItem_FileToOriginalActiveFolder_RestoresSuccessfully()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        var activeFolder = await SeedFolderAsync(db, _testUserId, null, "ActiveFolder", isDeleted: false);
        var deletedFile = await SeedFileAsync(db, _testUserId, activeFolder.Id, "my_doc.txt", 2048, isDeleted: true, deletedAt: DateTime.UtcNow);

        var result = await service.RestoreItemAsync(deletedFile.Id, _testUserId);

        Assert.Equal("File", result.ItemType);
        Assert.Equal(activeFolder.Id, result.RestoredToFolderId);
        Assert.False(result.RestoredToRoot);

        var fileInDb = await db.FileItems.FindAsync(deletedFile.Id);
        Assert.NotNull(fileInDb);
        Assert.False(fileInDb.IsDeleted);
        Assert.Null(fileInDb.DeletedAt);
        Assert.Equal(activeFolder.Id, fileInDb.FolderId);
    }

    [Fact]
    public async Task RestoreItem_FileWithDeletedParent_RestoresToRoot()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        var deletedFolder = await SeedFolderAsync(db, _testUserId, null, "OldDeletedFolder", isDeleted: true, deletedAt: DateTime.UtcNow);
        var deletedFile = await SeedFileAsync(db, _testUserId, deletedFolder.Id, "orphaned.txt", 1024, isDeleted: true, deletedAt: DateTime.UtcNow);

        var result = await service.RestoreItemAsync(deletedFile.Id, _testUserId);

        Assert.Equal("File", result.ItemType);
        Assert.Null(result.RestoredToFolderId);
        Assert.True(result.RestoredToRoot);

        var fileInDb = await db.FileItems.FindAsync(deletedFile.Id);
        Assert.NotNull(fileInDb);
        Assert.False(fileInDb.IsDeleted);
        Assert.Null(fileInDb.FolderId);
    }

    [Fact]
    public async Task RestoreItem_DuplicateNameInDestination_ThrowsValidationException()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        var activeFolder = await SeedFolderAsync(db, _testUserId, null, "Docs", isDeleted: false);
        // Active file already exists
        await SeedFileAsync(db, _testUserId, activeFolder.Id, "report.pdf", 500, isDeleted: false);
        // Deleted file with same name
        var deletedFile = await SeedFileAsync(db, _testUserId, activeFolder.Id, "report.pdf", 800, isDeleted: true, deletedAt: DateTime.UtcNow);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RestoreItemAsync(deletedFile.Id, _testUserId));
    }

    [Fact]
    public async Task RestoreItem_FolderWithChildren_RestoresEntireSubtree()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        var folder = await SeedFolderAsync(db, _testUserId, null, "Projects", isDeleted: true, deletedAt: DateTime.UtcNow);
        var subFolder = await SeedFolderAsync(db, _testUserId, folder.Id, "SubProject", isDeleted: true, deletedAt: DateTime.UtcNow);
        var fileInSub = await SeedFileAsync(db, _testUserId, subFolder.Id, "code.cs", 4000, isDeleted: true, deletedAt: DateTime.UtcNow);

        var result = await service.RestoreItemAsync(folder.Id, _testUserId);

        Assert.Equal("Folder", result.ItemType);
        Assert.Equal(1, result.RestoredSubfoldersCount);
        Assert.Equal(1, result.RestoredFilesCount);

        var restoredFolder = await db.Folders.FindAsync(folder.Id);
        var restoredSub = await db.Folders.FindAsync(subFolder.Id);
        var restoredFile = await db.FileItems.FindAsync(fileInSub.Id);

        Assert.False(restoredFolder!.IsDeleted);
        Assert.False(restoredSub!.IsDeleted);
        Assert.False(restoredFile!.IsDeleted);
    }

    [Fact]
    public async Task PermanentlyDeleteItem_File_ReleasesQuotaAndRemovesBlob()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        var file = await SeedFileAsync(db, _testUserId, null, "delete_me.txt", 5000, isDeleted: true, deletedAt: DateTime.UtcNow, blobRefCount: 1);
        var blobId = file.StorageBlobId;

        var userBefore = await db.Users.FindAsync(_testUserId);
        Assert.Equal(5000, userBefore!.UsedQuotaBytes);

        await service.PermanentlyDeleteItemAsync(file.Id, _testUserId);

        var fileInDb = await db.FileItems.FindAsync(file.Id);
        Assert.Null(fileInDb);

        var blobInDb = await db.StorageBlobs.FindAsync(blobId);
        Assert.Null(blobInDb);

        var userAfter = await db.Users.FindAsync(_testUserId);
        Assert.Equal(0, userAfter!.UsedQuotaBytes);
    }

    [Fact]
    public async Task PermanentlyDeleteItem_DeduplicatedFile_DecrementsRefCountWithoutRemovingBlob()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        // Blob shared across 2 files
        var file = await SeedFileAsync(db, _testUserId, null, "shared.txt", 3000, isDeleted: true, deletedAt: DateTime.UtcNow, blobRefCount: 2);
        var blobId = file.StorageBlobId;

        await service.PermanentlyDeleteItemAsync(file.Id, _testUserId);

        var fileInDb = await db.FileItems.FindAsync(file.Id);
        Assert.Null(fileInDb);

        // Blob should remain because ref count was 2
        var blobInDb = await db.StorageBlobs.FindAsync(blobId);
        Assert.NotNull(blobInDb);
        Assert.Equal(1, blobInDb.ReferenceCount);

        var userAfter = await db.Users.FindAsync(_testUserId);
        Assert.Equal(0, userAfter!.UsedQuotaBytes);
    }

    [Fact]
    public async Task PermanentlyDeleteItem_Folder_DeletesSubtreeAndReleasesQuota()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        var folder = await SeedFolderAsync(db, _testUserId, null, "FolderToDelete", isDeleted: true, deletedAt: DateTime.UtcNow);
        var subFolder = await SeedFolderAsync(db, _testUserId, folder.Id, "Child", isDeleted: true, deletedAt: DateTime.UtcNow);
        var file1 = await SeedFileAsync(db, _testUserId, folder.Id, "f1.txt", 2000, isDeleted: true, deletedAt: DateTime.UtcNow);
        var file2 = await SeedFileAsync(db, _testUserId, subFolder.Id, "f2.txt", 3000, isDeleted: true, deletedAt: DateTime.UtcNow);

        var userBefore = await db.Users.FindAsync(_testUserId);
        Assert.Equal(5000, userBefore!.UsedQuotaBytes);

        await service.PermanentlyDeleteItemAsync(folder.Id, _testUserId);

        Assert.Null(await db.Folders.FindAsync(folder.Id));
        Assert.Null(await db.Folders.FindAsync(subFolder.Id));
        Assert.Null(await db.FileItems.FindAsync(file1.Id));
        Assert.Null(await db.FileItems.FindAsync(file2.Id));

        var userAfter = await db.Users.FindAsync(_testUserId);
        Assert.Equal(0, userAfter!.UsedQuotaBytes);
    }

    [Fact]
    public async Task EmptyTrash_RemovesAllDeletedItemsAndReleasesQuota()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        var folder = await SeedFolderAsync(db, _testUserId, null, "TrashFolder", isDeleted: true, deletedAt: DateTime.UtcNow);
        var file1 = await SeedFileAsync(db, _testUserId, folder.Id, "trash1.txt", 4000, isDeleted: true, deletedAt: DateTime.UtcNow);
        var file2 = await SeedFileAsync(db, _testUserId, null, "trash2.txt", 6000, isDeleted: true, deletedAt: DateTime.UtcNow);

        // Also add an active file that should NOT be deleted
        var activeFile = await SeedFileAsync(db, _testUserId, null, "active.txt", 1000, isDeleted: false);

        var userBefore = await db.Users.FindAsync(_testUserId);
        Assert.Equal(11000, userBefore!.UsedQuotaBytes);

        var result = await service.EmptyTrashAsync(_testUserId);

        Assert.Equal(2, result.DeletedFilesCount);
        Assert.Equal(1, result.DeletedFoldersCount);
        Assert.Equal(10000, result.FreedBytes);

        Assert.Null(await db.Folders.FindAsync(folder.Id));
        Assert.Null(await db.FileItems.FindAsync(file1.Id));
        Assert.Null(await db.FileItems.FindAsync(file2.Id));

        var activeInDb = await db.FileItems.FindAsync(activeFile.Id);
        Assert.NotNull(activeInDb);

        var userAfter = await db.Users.FindAsync(_testUserId);
        Assert.Equal(1000, userAfter!.UsedQuotaBytes);
    }

    [Fact]
    public async Task CleanupExpiredTrash_OlderThanCutoff_PurgesExpiredItemsOnly()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        var oldTime = DateTime.UtcNow.AddDays(-35); // > 30 days
        var recentTime = DateTime.UtcNow.AddDays(-5); // < 30 days

        var expiredFolder = await SeedFolderAsync(db, _testUserId, null, "ExpiredFolder", isDeleted: true, deletedAt: oldTime);
        var expiredFile = await SeedFileAsync(db, _testUserId, null, "expired.txt", 7000, isDeleted: true, deletedAt: oldTime);

        var recentFile = await SeedFileAsync(db, _testUserId, null, "recent.txt", 3000, isDeleted: true, deletedAt: recentTime);

        var result = await service.CleanupExpiredTrashAsync(TimeSpan.FromDays(30));

        Assert.Equal(1, result.PurgedFilesCount);
        Assert.Equal(1, result.PurgedFoldersCount);
        Assert.Equal(7000, result.FreedBytes);

        Assert.Null(await db.Folders.FindAsync(expiredFolder.Id));
        Assert.Null(await db.FileItems.FindAsync(expiredFile.Id));

        var recentInDb = await db.FileItems.FindAsync(recentFile.Id);
        Assert.NotNull(recentInDb);

        var userAfter = await db.Users.FindAsync(_testUserId);
        Assert.Equal(3000, userAfter!.UsedQuotaBytes);
    }

    [Fact]
    public async Task EmptyTrash_WhenAlreadyEmpty_IsIdempotentAndReturnsZero()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        var result = await service.EmptyTrashAsync(_testUserId);

        Assert.Equal(0, result.DeletedFilesCount);
        Assert.Equal(0, result.DeletedFoldersCount);
        Assert.Equal(0, result.FreedBytes);
    }

    [Fact]
    public async Task RestoreItem_ItemNotFound_ThrowsNotFoundException()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.RestoreItemAsync(Guid.NewGuid(), _testUserId));
    }

    [Fact]
    public async Task PermanentlyDeleteItem_ItemNotFound_ThrowsNotFoundException()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.PermanentlyDeleteItemAsync(Guid.NewGuid(), _testUserId));
    }

    [Fact]
    public async Task GetTrashContent_InactiveUser_ThrowsUnauthorizedException()
    {
        using var db = CreateDbContext();
        var user = await db.Users.FindAsync(_testUserId);
        user!.IsActive = false;
        await db.SaveChangesAsync();

        var service = new TrashService(db, _resolver);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.GetTrashContentAsync(_testUserId));
    }

    [Fact]
    public async Task EmptyTrash_WithDeduplicatedBlob_DecrementsRefCountWithoutDeletingBlob()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        // File in trash and active file sharing the same blob
        var trashFile = await SeedFileAsync(db, _testUserId, null, "trash_shared.txt", 4000, isDeleted: true, deletedAt: DateTime.UtcNow, blobRefCount: 2);
        var activeFile = await SeedFileAsync(db, _testUserId, null, "active_shared.txt", 4000, isDeleted: false, blobRefCount: 2);
        // Link both files to same blob
        activeFile.StorageBlobId = trashFile.StorageBlobId;
        await db.SaveChangesAsync();

        var blobId = trashFile.StorageBlobId;

        var result = await service.EmptyTrashAsync(_testUserId);
        Assert.Equal(1, result.DeletedFilesCount);

        // Blob should still exist with reference count 1
        var blobInDb = await db.StorageBlobs.FindAsync(blobId);
        Assert.NotNull(blobInDb);
        Assert.Equal(1, blobInDb.ReferenceCount);
    }

    [Fact]
    public async Task PermanentlyDeleteItem_InactiveUser_ThrowsUnauthorizedException()
    {
        using var db = CreateDbContext();
        var user = await db.Users.FindAsync(_testUserId);
        user!.IsActive = false;
        await db.SaveChangesAsync();

        var service = new TrashService(db, _resolver);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.PermanentlyDeleteItemAsync(Guid.NewGuid(), _testUserId));
    }

    [Fact]
    public async Task EmptyTrash_InactiveUser_ThrowsUnauthorizedException()
    {
        using var db = CreateDbContext();
        var user = await db.Users.FindAsync(_testUserId);
        user!.IsActive = false;
        await db.SaveChangesAsync();

        var service = new TrashService(db, _resolver);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.EmptyTrashAsync(_testUserId));
    }

    [Fact]
    public async Task EmptyTrash_MultipleDeletedFilesSharingSameBlob_DeletesBlobSafelyWithoutException()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        // 3 files in trash sharing the same blob
        var file1 = await SeedFileAsync(db, _testUserId, null, "f1.txt", 2000, isDeleted: true, deletedAt: DateTime.UtcNow, blobRefCount: 3);
        var file2 = await SeedFileAsync(db, _testUserId, null, "f2.txt", 2000, isDeleted: true, deletedAt: DateTime.UtcNow, blobRefCount: 3);
        var file3 = await SeedFileAsync(db, _testUserId, null, "f3.txt", 2000, isDeleted: true, deletedAt: DateTime.UtcNow, blobRefCount: 3);

        // Link all to file1's blob
        file2.StorageBlobId = file1.StorageBlobId;
        file3.StorageBlobId = file1.StorageBlobId;
        await db.SaveChangesAsync();

        var blobId = file1.StorageBlobId;

        var result = await service.EmptyTrashAsync(_testUserId);

        Assert.Equal(3, result.DeletedFilesCount);
        Assert.Equal(6000, result.FreedBytes);
        Assert.Null(await db.StorageBlobs.FindAsync(blobId));
    }

    [Fact]
    public async Task PermanentlyDeleteItem_FolderSubtreeFilesSharingSameBlob_DeletesBlobSafely()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        var folder = await SeedFolderAsync(db, _testUserId, null, "SharedBlobFolder", isDeleted: true, deletedAt: DateTime.UtcNow);
        var subFolder = await SeedFolderAsync(db, _testUserId, folder.Id, "Sub", isDeleted: true, deletedAt: DateTime.UtcNow);

        var file1 = await SeedFileAsync(db, _testUserId, folder.Id, "shared1.txt", 1500, isDeleted: true, deletedAt: DateTime.UtcNow, blobRefCount: 2);
        var file2 = await SeedFileAsync(db, _testUserId, subFolder.Id, "shared2.txt", 1500, isDeleted: true, deletedAt: DateTime.UtcNow, blobRefCount: 2);

        file2.StorageBlobId = file1.StorageBlobId;
        await db.SaveChangesAsync();

        var blobId = file1.StorageBlobId;

        await service.PermanentlyDeleteItemAsync(folder.Id, _testUserId);

        Assert.Null(await db.Folders.FindAsync(folder.Id));
        Assert.Null(await db.Folders.FindAsync(subFolder.Id));
        Assert.Null(await db.FileItems.FindAsync(file1.Id));
        Assert.Null(await db.FileItems.FindAsync(file2.Id));
        Assert.Null(await db.StorageBlobs.FindAsync(blobId));
    }

    [Fact]
    public async Task CleanupExpiredTrash_MultipleExpiredFilesSharingSameBlob_PurgesBlobsSafely()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        var oldTime = DateTime.UtcNow.AddDays(-35);
        var file1 = await SeedFileAsync(db, _testUserId, null, "exp1.txt", 1000, isDeleted: true, deletedAt: oldTime, blobRefCount: 2);
        var file2 = await SeedFileAsync(db, _testUserId, null, "exp2.txt", 1000, isDeleted: true, deletedAt: oldTime, blobRefCount: 2);

        file2.StorageBlobId = file1.StorageBlobId;
        await db.SaveChangesAsync();

        var blobId = file1.StorageBlobId;

        var result = await service.CleanupExpiredTrashAsync(TimeSpan.FromDays(30));

        Assert.Equal(2, result.PurgedFilesCount);
        Assert.Equal(1, result.PurgedBlobsCount);
        Assert.Null(await db.StorageBlobs.FindAsync(blobId));
    }

    [Fact]
    public async Task CleanupExpiredTrash_WithChildFolderRemaining_ReparentsChildSafelyWithoutFkViolation()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        var oldTime = DateTime.UtcNow.AddDays(-40);
        var recentTime = DateTime.UtcNow.AddDays(-5);

        // Parent expired, child in trash but not yet expired
        var parentFolder = await SeedFolderAsync(db, _testUserId, null, "ExpiredParent", isDeleted: true, deletedAt: oldTime);
        var childFolder = await SeedFolderAsync(db, _testUserId, parentFolder.Id, "UnexpiredChild", isDeleted: true, deletedAt: recentTime);

        var result = await service.CleanupExpiredTrashAsync(TimeSpan.FromDays(30));

        Assert.Equal(1, result.PurgedFoldersCount);
        Assert.Null(await db.Folders.FindAsync(parentFolder.Id));

        var remainingChild = await db.Folders.FindAsync(childFolder.Id);
        Assert.NotNull(remainingChild);
        Assert.Null(remainingChild.ParentFolderId); // Safely reparented to avoid dangling pointer or FK error
    }

    [Fact]
    public async Task RestoreItem_FolderWithConflictingFileNamesInTrash_RestoresLatestAndAvoidsDuplicateName()
    {
        using var db = CreateDbContext();
        var service = new TrashService(db, _resolver);

        var folder = await SeedFolderAsync(db, _testUserId, null, "FolderWithDuplicates", isDeleted: true, deletedAt: DateTime.UtcNow);

        // Two deleted files with the exact same name inside the same deleted folder (deleted at different times)
        var olderFile = await SeedFileAsync(db, _testUserId, folder.Id, "document.docx", 2000, isDeleted: true, deletedAt: DateTime.UtcNow.AddHours(-2));
        var newerFile = await SeedFileAsync(db, _testUserId, folder.Id, "document.docx", 2500, isDeleted: true, deletedAt: DateTime.UtcNow.AddMinutes(-5));

        var result = await service.RestoreItemAsync(folder.Id, _testUserId);

        Assert.Equal(1, result.RestoredFilesCount);

        var restoredNewer = await db.FileItems.FindAsync(newerFile.Id);
        var unrestoredOlder = await db.FileItems.FindAsync(olderFile.Id);

        Assert.False(restoredNewer!.IsDeleted);
        Assert.True(unrestoredOlder!.IsDeleted); // Older stays in trash to prevent collision
    }
}
