using Microsoft.EntityFrameworkCore;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.DTOs.Folders;
using OmegaCloud.Application.Services;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;
using OmegaCloud.Infrastructure.Data;

namespace OmegaCloud.Tests.Services;

public class FolderServiceTests
{
    private readonly Guid _testUserId = Guid.NewGuid();

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);

        // Seed test user
        var user = new User
        {
            Id = _testUserId,
            Username = "testuser",
            Email = "test@omegacloud.local",
            PasswordHash = "hash",
            Role = UserRole.User,
            AllocatedQuotaBytes = 100_000_000L,
            UsedQuotaBytes = 25_000_000L,
            IsActive = true
        };

        context.Users.Add(user);
        context.SaveChanges();

        return context;
    }

    [Fact]
    public async Task GetFolderContentAsync_RootFolder_ReturnsRootItemsAndQuota()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FolderService(db);

        var rootFolder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            ParentFolderId = null,
            Name = "Documents",
            IsDeleted = false
        };
        db.Folders.Add(rootFolder);

        var blob = new StorageBlob
        {
            Id = Guid.NewGuid(),
            StorageNodeId = Guid.NewGuid(),
            Sha256Hash = "abc",
            SizeBytes = 1024,
            StorageRelativePath = "blobs/ab/cd/test.blob"
        };
        db.StorageBlobs.Add(blob);

        var rootFile = new FileItem
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            FolderId = null,
            StorageBlobId = blob.Id,
            Name = "notes.txt",
            Extension = "txt",
            MimeType = "text/plain",
            SizeBytes = 1024,
            IsDeleted = false
        };
        db.FileItems.Add(rootFile);
        await db.SaveChangesAsync();

        // Act
        var result = await service.GetFolderContentAsync(null, _testUserId);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.CurrentFolder);
        Assert.Single(result.Breadcrumbs);
        Assert.Equal("Root", result.Breadcrumbs[0].Name);
        Assert.Single(result.Folders);
        Assert.Equal("Documents", result.Folders[0].Name);
        Assert.Single(result.Files);
        Assert.Equal("notes.txt", result.Files[0].Name);
        Assert.Equal(100_000_000L, result.Quota.AllocatedBytes);
        Assert.Equal(25_000_000L, result.Quota.UsedBytes);
        Assert.Equal(75_000_000L, result.Quota.AvailableBytes);
    }

    [Fact]
    public async Task GetFolderContentAsync_SubFolder_ReturnsCorrectBreadcrumbsAndSubItems()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FolderService(db);

        var parent = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            ParentFolderId = null,
            Name = "Parent",
            IsDeleted = false
        };
        var child = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            ParentFolderId = parent.Id,
            Name = "Child",
            IsDeleted = false
        };
        db.Folders.AddRange(parent, child);
        await db.SaveChangesAsync();

        // Act
        var result = await service.GetFolderContentAsync(child.Id, _testUserId);

        // Assert
        Assert.NotNull(result.CurrentFolder);
        Assert.Equal("Child", result.CurrentFolder.Name);
        Assert.Equal(3, result.Breadcrumbs.Count);
        Assert.Equal("Root", result.Breadcrumbs[0].Name);
        Assert.Equal("Parent", result.Breadcrumbs[1].Name);
        Assert.Equal("Child", result.Breadcrumbs[2].Name);
    }

    [Fact]
    public async Task GetFolderContentAsync_NonExistentFolder_ThrowsNotFoundException()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FolderService(db);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetFolderContentAsync(Guid.NewGuid(), _testUserId));
    }

    [Fact]
    public async Task CreateFolderAsync_RootFolder_CreatesSuccessfully()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FolderService(db);

        var request = new CreateFolderRequest
        {
            Name = "Projects",
            ParentFolderId = null,
            Color = "#007AFF"
        };

        // Act
        var result = await service.CreateFolderAsync(request, _testUserId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Projects", result.Name);
        Assert.Null(result.ParentFolderId);
        Assert.Equal("#007AFF", result.Color);

        var inDb = await db.Folders.FirstOrDefaultAsync(f => f.Id == result.Id);
        Assert.NotNull(inDb);
        Assert.Equal("Projects", inDb.Name);
    }

    [Fact]
    public async Task CreateFolderAsync_DuplicateNameInSameParent_ThrowsValidationException()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FolderService(db);

        await service.CreateFolderAsync(new CreateFolderRequest { Name = "Duplicate" }, _testUserId);

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateFolderAsync(new CreateFolderRequest { Name = "Duplicate" }, _testUserId));
    }

    [Fact]
    public async Task CreateFolderAsync_SameNameInDifferentParents_Succeeds()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FolderService(db);

        var parent1 = await service.CreateFolderAsync(new CreateFolderRequest { Name = "Parent1" }, _testUserId);
        var parent2 = await service.CreateFolderAsync(new CreateFolderRequest { Name = "Parent2" }, _testUserId);

        // Act
        var sub1 = await service.CreateFolderAsync(new CreateFolderRequest { Name = "Shared", ParentFolderId = parent1.Id }, _testUserId);
        var sub2 = await service.CreateFolderAsync(new CreateFolderRequest { Name = "Shared", ParentFolderId = parent2.Id }, _testUserId);

        // Assert
        Assert.NotNull(sub1);
        Assert.NotNull(sub2);
        Assert.NotEqual(sub1.Id, sub2.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("invalid/folder")]
    [InlineData("invalid\\folder")]
    [InlineData("invalid:folder")]
    [InlineData("invalid*folder")]
    public async Task CreateFolderAsync_InvalidNames_ThrowsValidationException(string folderName)
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FolderService(db);

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateFolderAsync(new CreateFolderRequest { Name = folderName }, _testUserId));
    }

    [Fact]
    public async Task UpdateFolderAsync_RenameAndColor_UpdatesCorrectly()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FolderService(db);
        var folder = await service.CreateFolderAsync(new CreateFolderRequest { Name = "OldName", Color = "blue" }, _testUserId);

        // Act
        var updated = await service.UpdateFolderAsync(folder.Id, new UpdateFolderRequest
        {
            Name = "NewName",
            Color = "purple"
        }, _testUserId);

        // Assert
        Assert.Equal("NewName", updated.Name);
        Assert.Equal("purple", updated.Color);
    }

    [Fact]
    public async Task UpdateFolderAsync_MoveToAnotherFolder_UpdatesParentFolderId()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FolderService(db);
        var folder1 = await service.CreateFolderAsync(new CreateFolderRequest { Name = "Folder1" }, _testUserId);
        var folder2 = await service.CreateFolderAsync(new CreateFolderRequest { Name = "Folder2" }, _testUserId);

        // Act
        var moved = await service.UpdateFolderAsync(folder1.Id, new UpdateFolderRequest
        {
            ParentFolderId = folder2.Id
        }, _testUserId);

        // Assert
        Assert.Equal(folder2.Id, moved.ParentFolderId);
    }

    [Fact]
    public async Task UpdateFolderAsync_MoveIntoItself_ThrowsValidationException()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FolderService(db);
        var folder = await service.CreateFolderAsync(new CreateFolderRequest { Name = "Folder1" }, _testUserId);

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.UpdateFolderAsync(folder.Id, new UpdateFolderRequest
            {
                ParentFolderId = folder.Id
            }, _testUserId));
    }

    [Fact]
    public async Task UpdateFolderAsync_MoveIntoDescendant_ThrowsValidationException()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FolderService(db);
        var top = await service.CreateFolderAsync(new CreateFolderRequest { Name = "Top" }, _testUserId);
        var middle = await service.CreateFolderAsync(new CreateFolderRequest { Name = "Middle", ParentFolderId = top.Id }, _testUserId);
        var bottom = await service.CreateFolderAsync(new CreateFolderRequest { Name = "Bottom", ParentFolderId = middle.Id }, _testUserId);

        // Act & Assert: Attempt to move 'top' into 'bottom'
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.UpdateFolderAsync(top.Id, new UpdateFolderRequest
            {
                ParentFolderId = bottom.Id
            }, _testUserId));
    }

    [Fact]
    public async Task DeleteFolderAsync_CascadesSoftDeleteToChildrenAndFiles()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FolderService(db);

        var parent = await service.CreateFolderAsync(new CreateFolderRequest { Name = "Parent" }, _testUserId);
        var child = await service.CreateFolderAsync(new CreateFolderRequest { Name = "Child", ParentFolderId = parent.Id }, _testUserId);

        var blob = new StorageBlob
        {
            Id = Guid.NewGuid(),
            StorageNodeId = Guid.NewGuid(),
            Sha256Hash = "abc",
            SizeBytes = 512,
            StorageRelativePath = "blobs/ab/cd/test.blob"
        };
        db.StorageBlobs.Add(blob);

        var fileInChild = new FileItem
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            FolderId = child.Id,
            StorageBlobId = blob.Id,
            Name = "deep_file.txt",
            Extension = "txt",
            MimeType = "text/plain",
            SizeBytes = 512,
            IsDeleted = false
        };
        db.FileItems.Add(fileInChild);
        await db.SaveChangesAsync();

        // Act
        await service.DeleteFolderAsync(parent.Id, _testUserId);

        // Assert
        var parentDb = await db.Folders.FirstAsync(f => f.Id == parent.Id);
        var childDb = await db.Folders.FirstAsync(f => f.Id == child.Id);
        var fileDb = await db.FileItems.FirstAsync(f => f.Id == fileInChild.Id);

        Assert.True(parentDb.IsDeleted);
        Assert.NotNull(parentDb.DeletedAt);
        Assert.True(childDb.IsDeleted);
        Assert.NotNull(childDb.DeletedAt);
        Assert.True(fileDb.IsDeleted);
        Assert.NotNull(fileDb.DeletedAt);
    }
}
