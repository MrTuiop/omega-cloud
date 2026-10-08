using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Application.Services;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;
using OmegaCloud.Infrastructure.Data;
using OmegaCloud.Infrastructure.Storage;

namespace OmegaCloud.Tests.Services;

public class FileServiceTests : IDisposable
{
    private readonly Guid _testUserId = Guid.NewGuid();
    private readonly string _tempStorageDirectory;
    private readonly LocalDiskStorageDriver _driver;
    private readonly StorageDriverResolver _resolver;
    private readonly StorageNode _localNode;

    public FileServiceTests()
    {
        _tempStorageDirectory = Path.Combine(Path.GetTempPath(), "FileServiceTests_" + Guid.NewGuid().ToString("N"));
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

        var context = new AppDbContext(options);

        // Seed user and storage node
        var user = new User
        {
            Id = _testUserId,
            Username = "filetester",
            Email = "filetester@omegacloud.local",
            PasswordHash = "hash",
            Role = UserRole.User,
            AllocatedQuotaBytes = 10_000_000L,
            UsedQuotaBytes = 0,
            IsActive = true
        };

        context.Users.Add(user);
        context.StorageNodes.Add(_localNode);
        context.SaveChanges();

        return context;
    }

    [Fact]
    public async Task UploadFileAsync_RootFolder_UploadsAndUpdatesQuota()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FileService(db, _resolver);

        var contentBytes = Encoding.UTF8.GetBytes("Hello Omega Cloud");
        using var contentStream = new MemoryStream(contentBytes);

        var request = new UploadFileStreamRequest
        {
            Content = contentStream,
            FileName = "hello.txt",
            ContentType = "text/plain",
            SizeBytes = contentBytes.Length,
            FolderId = null
        };

        // Act
        var result = await service.UploadFileAsync(request, _testUserId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("hello.txt", result.Name);
        Assert.Equal("txt", result.Extension);
        Assert.Equal("text/plain", result.MimeType);
        Assert.Equal(contentBytes.Length, result.SizeBytes);
        Assert.Null(result.FolderId);

        var userInDb = await db.Users.FirstAsync(u => u.Id == _testUserId);
        Assert.Equal(contentBytes.Length, userInDb.UsedQuotaBytes);

        var blobInDb = await db.StorageBlobs.FirstOrDefaultAsync(b => b.Id == result.StorageBlobId);
        Assert.NotNull(blobInDb);
        Assert.Equal(1, blobInDb.ReferenceCount);
    }

    [Fact]
    public async Task UploadFileAsync_ExceedingQuota_ThrowsValidationException()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FileService(db, _resolver);

        var user = await db.Users.FirstAsync(u => u.Id == _testUserId);
        user.UsedQuotaBytes = 9_999_900L; // 100 bytes remaining out of 10,000,000
        await db.SaveChangesAsync();

        var contentBytes = new byte[200];
        using var contentStream = new MemoryStream(contentBytes);

        var request = new UploadFileStreamRequest
        {
            Content = contentStream,
            FileName = "large.bin",
            SizeBytes = 200
        };

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.UploadFileAsync(request, _testUserId));
    }

    [Fact]
    public async Task UploadFileAsync_IdenticalContent_DeduplicatesBlobAndIncrementsReferenceCount()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FileService(db, _resolver);

        var contentBytes = Encoding.UTF8.GetBytes("Identical file content for CAS test");

        using var stream1 = new MemoryStream(contentBytes);
        var req1 = new UploadFileStreamRequest
        {
            Content = stream1,
            FileName = "file1.txt",
            SizeBytes = contentBytes.Length
        };

        using var stream2 = new MemoryStream(contentBytes);
        var req2 = new UploadFileStreamRequest
        {
            Content = stream2,
            FileName = "file2.txt",
            SizeBytes = contentBytes.Length
        };

        // Act
        var result1 = await service.UploadFileAsync(req1, _testUserId);
        var result2 = await service.UploadFileAsync(req2, _testUserId);

        // Assert
        Assert.NotEqual(result1.Id, result2.Id);
        Assert.Equal(result1.StorageBlobId, result2.StorageBlobId);

        var blob = await db.StorageBlobs.FirstAsync(b => b.Id == result1.StorageBlobId);
        Assert.Equal(2, blob.ReferenceCount);
    }

    [Fact]
    public async Task GetFileMetadataAsync_ReturnsCompleteMetadata()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FileService(db, _resolver);

        var content = Encoding.UTF8.GetBytes("Metadata check");
        using var stream = new MemoryStream(content);
        var uploaded = await service.UploadFileAsync(new UploadFileStreamRequest
        {
            Content = stream,
            FileName = "doc.pdf",
            ContentType = "application/pdf",
            SizeBytes = content.Length
        }, _testUserId);

        // Act
        var meta = await service.GetFileMetadataAsync(uploaded.Id, _testUserId);

        // Assert
        Assert.NotNull(meta);
        Assert.Equal(uploaded.Id, meta.Id);
        Assert.Equal("doc.pdf", meta.Name);
        Assert.Equal("pdf", meta.Extension);
        Assert.Equal("application/pdf", meta.MimeType);
        Assert.Equal(_localNode.Name, meta.StorageNodeName);
        Assert.NotEmpty(meta.Sha256Hash);
    }

    [Fact]
    public async Task GetFileDownloadStreamAsync_ReturnsOriginalContent()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FileService(db, _resolver);

        var originalBytes = Encoding.UTF8.GetBytes("Download byte-for-byte stream test");
        using var stream = new MemoryStream(originalBytes);
        var uploaded = await service.UploadFileAsync(new UploadFileStreamRequest
        {
            Content = stream,
            FileName = "download.bin",
            SizeBytes = originalBytes.Length
        }, _testUserId);

        // Act
        var downloadResult = await service.GetFileDownloadStreamAsync(uploaded.Id, _testUserId);

        // Assert
        Assert.NotNull(downloadResult.Stream);
        Assert.False(downloadResult.IsRedirect);
        using var ms = new MemoryStream();
        await downloadResult.Stream.CopyToAsync(ms);
        Assert.Equal(originalBytes, ms.ToArray());
    }

    [Fact]
    public async Task UpdateFileAsync_RenameAndMove_WorksCorrectly()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FileService(db, _resolver);

        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "TargetFolder"
        };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        var content = Encoding.UTF8.GetBytes("Rename test");
        using var stream = new MemoryStream(content);
        var uploaded = await service.UploadFileAsync(new UploadFileStreamRequest
        {
            Content = stream,
            FileName = "oldname.txt",
            SizeBytes = content.Length
        }, _testUserId);

        // Act
        var updated = await service.UpdateFileAsync(uploaded.Id, new UpdateFileRequest
        {
            Name = "newname.md",
            FolderId = folder.Id,
            IsFavorite = true
        }, _testUserId);

        // Assert
        Assert.Equal("newname.md", updated.Name);
        Assert.Equal("md", updated.Extension);
        Assert.Equal(folder.Id, updated.FolderId);
        Assert.True(updated.IsFavorite);
    }

    [Fact]
    public async Task DeleteFileAsync_SoftDeletesFile()
    {
        // Arrange
        using var db = CreateDbContext();
        var service = new FileService(db, _resolver);

        var content = Encoding.UTF8.GetBytes("Delete test");
        using var stream = new MemoryStream(content);
        var uploaded = await service.UploadFileAsync(new UploadFileStreamRequest
        {
            Content = stream,
            FileName = "delete_me.txt",
            SizeBytes = content.Length
        }, _testUserId);

        // Act
        await service.DeleteFileAsync(uploaded.Id, _testUserId);

        // Assert
        var fileDb = await db.FileItems.FirstAsync(f => f.Id == uploaded.Id);
        Assert.True(fileDb.IsDeleted);
        Assert.NotNull(fileDb.DeletedAt);

        // Subsequent get metadata throws NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetFileMetadataAsync(uploaded.Id, _testUserId));
    }
}
