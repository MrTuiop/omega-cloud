using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Application.DTOs.Upload;
using OmegaCloud.Application.Services;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;
using OmegaCloud.Infrastructure.Data;
using OmegaCloud.Infrastructure.Storage;

namespace OmegaCloud.Tests.Services;

public class UploadServiceTests : IDisposable
{
    private readonly Guid _testUserId = Guid.NewGuid();
    private readonly string _tempStorageDirectory;
    private readonly LocalDiskStorageDriver _driver;
    private readonly StorageDriverResolver _resolver;
    private readonly StorageNode _localNode;

    public UploadServiceTests()
    {
        _tempStorageDirectory = Path.Combine(Path.GetTempPath(), "UploadServiceTests_" + Guid.NewGuid().ToString("N"));
        _driver = new LocalDiskStorageDriver(_tempStorageDirectory, NullLogger<LocalDiskStorageDriver>.Instance);
        _resolver = new StorageDriverResolver(_driver);

        _localNode = new StorageNode
        {
            Id = Guid.NewGuid(),
            Name = "Local Storage Node",
            RegistrationKey = "local-key-m3",
            NodeSecretHash = "secret-m3",
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

    private AppDbContext CreateDbContext(long allocatedQuota = 100_000_000L, long usedQuota = 0)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);

        var user = new User
        {
            Id = _testUserId,
            Username = "uploadtester",
            Email = "uploadtester@omegacloud.local",
            PasswordHash = "hash",
            Role = UserRole.User,
            AllocatedQuotaBytes = allocatedQuota,
            UsedQuotaBytes = usedQuota,
            IsActive = true
        };

        context.Users.Add(user);
        context.StorageNodes.Add(_localNode);
        context.SaveChanges();

        return context;
    }

    [Fact]
    public async Task DirectStreamUploadAsync_FileExceeds50MB_ThrowsValidationException()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var request = new UploadFileStreamRequest
        {
            Content = new MemoryStream(),
            FileName = "large_direct.bin",
            SizeBytes = 50 * 1024 * 1024 + 1
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.DirectStreamUploadAsync(request, _testUserId));
        Assert.Contains("50 MB limit", ex.Message);
    }

    [Fact]
    public async Task DirectStreamUploadAsync_ValidSmallFile_UploadsSuccessfully()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var contentBytes = Encoding.UTF8.GetBytes("Small payload");
        using var contentStream = new MemoryStream(contentBytes);

        var request = new UploadFileStreamRequest
        {
            Content = contentStream,
            FileName = "small.txt",
            ContentType = "text/plain",
            SizeBytes = contentBytes.Length
        };

        // Act
        var result = await uploadService.DirectStreamUploadAsync(request, _testUserId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("small.txt", result.Name);
        Assert.Equal(contentBytes.Length, result.SizeBytes);

        var user = await db.Users.FindAsync(_testUserId);
        Assert.Equal(contentBytes.Length, user!.UsedQuotaBytes);
    }

    [Fact]
    public async Task InitSessionAsync_ExceedsUserQuota_ThrowsValidationException()
    {
        // Arrange (10 MB quota, 9 MB used -> 1 MB remaining)
        using var db = CreateDbContext(allocatedQuota: 10_000_000L, usedQuota: 9_000_000L);
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var request = new InitUploadSessionRequest
        {
            FileName = "video.mp4",
            TotalSizeBytes = 2_000_000L // 2 MB > 1 MB remaining
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.InitSessionAsync(request, _testUserId));
        Assert.Contains("quota exceeded", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitSessionAsync_InvalidFileName_ThrowsValidationException()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var request = new InitUploadSessionRequest
        {
            FileName = "../evil.sh",
            TotalSizeBytes = 1000
        };

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.InitSessionAsync(request, _testUserId));
    }

    [Fact]
    public async Task InitSessionAsync_NonExistentFolder_ThrowsNotFoundException()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var request = new InitUploadSessionRequest
        {
            FileName = "document.pdf",
            TotalSizeBytes = 1000,
            FolderId = Guid.NewGuid()
        };

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            uploadService.InitSessionAsync(request, _testUserId));
    }

    [Fact]
    public async Task InitSessionAsync_DuplicateFileNameInFolder_ThrowsValidationException()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        // Add existing file
        db.FileItems.Add(new FileItem
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            FolderId = null,
            StorageBlobId = Guid.NewGuid(),
            Name = "duplicate.txt",
            Extension = "txt",
            MimeType = "text/plain",
            SizeBytes = 500,
            IsDeleted = false
        });
        await db.SaveChangesAsync();

        var request = new InitUploadSessionRequest
        {
            FileName = "duplicate.txt",
            TotalSizeBytes = 500
        };

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.InitSessionAsync(request, _testUserId));
    }

    [Fact]
    public async Task InitSessionAsync_ValidRequest_CreatesSessionWithCorrectDefaults()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var request = new InitUploadSessionRequest
        {
            FileName = "archive.tar",
            TotalSizeBytes = 50_000_000L // 50 MB -> with 20 MB chunk size = 3 chunks
        };

        // Act
        var session = await uploadService.InitSessionAsync(request, _testUserId);

        // Assert
        Assert.NotNull(session);
        Assert.Equal("archive.tar", session.FileName);
        Assert.Equal(50_000_000L, session.TotalSizeBytes);
        Assert.Equal(20 * 1024 * 1024, session.ChunkSizeBytes);
        Assert.Equal(3, session.TotalChunks);
        Assert.Equal("Proxy", session.UploadMode);
        Assert.Equal("Created", session.Status);
        Assert.Equal(3, session.MissingChunks.Count);
        Assert.Empty(session.UploadedChunks);

        var entity = await db.UploadSessions.FindAsync(session.SessionId);
        Assert.NotNull(entity);
        Assert.Equal(UploadSessionStatus.Created, entity.Status);
    }

    [Fact]
    public async Task UploadChunkAsync_InvalidIndex_ThrowsValidationException()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var sessionDto = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "test.bin",
            TotalSizeBytes = 100,
            ChunkSizeBytes = 50,
            TotalChunks = 2
        }, _testUserId);

        using var chunkStream = new MemoryStream(Encoding.UTF8.GetBytes("chunk data"));

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.UploadChunkAsync(sessionDto.SessionId, 5, chunkStream, null, null, _testUserId));
    }

    [Fact]
    public async Task UploadChunkAsync_ValidChunk_SavesAndUpdatesStatusToUploading()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var sessionDto = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "test.bin",
            TotalSizeBytes = 100,
            ChunkSizeBytes = 50,
            TotalChunks = 2
        }, _testUserId);

        var chunkBytes = Encoding.UTF8.GetBytes("chunk 0 data");
        using var chunkStream = new MemoryStream(chunkBytes);

        // Act
        var chunkResult = await uploadService.UploadChunkAsync(sessionDto.SessionId, 0, chunkStream, chunkBytes.Length, null, _testUserId);

        // Assert
        Assert.NotNull(chunkResult);
        Assert.Equal(0, chunkResult.ChunkIndex);
        Assert.Equal(chunkBytes.Length, chunkResult.SizeBytes);
        Assert.Equal("Saved", chunkResult.Status);

        var entity = await db.UploadSessions.Include(s => s.Chunks).FirstOrDefaultAsync(s => s.Id == sessionDto.SessionId);
        Assert.Equal(UploadSessionStatus.Uploading, entity!.Status);
        Assert.Single(entity.Chunks);
        Assert.Equal(0, entity.Chunks.First().ChunkIndex);
    }

    [Fact]
    public async Task UploadChunkAsync_MismatchedExpectedSha256_ThrowsValidationException()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var sessionDto = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "test.bin",
            TotalSizeBytes = 100,
            ChunkSizeBytes = 50,
            TotalChunks = 2
        }, _testUserId);

        var chunkBytes = Encoding.UTF8.GetBytes("chunk 0 data");
        using var chunkStream = new MemoryStream(chunkBytes);

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.UploadChunkAsync(sessionDto.SessionId, 0, chunkStream, chunkBytes.Length, "badhash0000000000", _testUserId));
    }

    [Fact]
    public async Task CompleteSessionAsync_MissingChunks_ThrowsValidationException()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var sessionDto = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "test.bin",
            TotalSizeBytes = 100,
            ChunkSizeBytes = 50,
            TotalChunks = 2
        }, _testUserId);

        var chunkBytes = Encoding.UTF8.GetBytes("50 bytes of chunk 0 dummy data....................");
        using var chunkStream = new MemoryStream(chunkBytes);
        await uploadService.UploadChunkAsync(sessionDto.SessionId, 0, chunkStream, chunkBytes.Length, null, _testUserId);

        // Act & Assert (chunk 1 is missing)
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.CompleteSessionAsync(sessionDto.SessionId, null, _testUserId));
        Assert.Contains("missing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteSessionAsync_AllChunksPresent_AssemblesFileItem_AndIncrementsQuota()
    {
        // Arrange
        using var db = CreateDbContext(allocatedQuota: 10_000_000L, usedQuota: 0);
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var part1 = Encoding.UTF8.GetBytes("Hello ");
        var part2 = Encoding.UTF8.GetBytes("Omega Cloud Chunks!");
        var fullBytes = Encoding.UTF8.GetBytes("Hello Omega Cloud Chunks!");
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(fullBytes)).ToLowerInvariant();

        var sessionDto = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "greeting.txt",
            TotalSizeBytes = fullBytes.Length,
            ChunkSizeBytes = part1.Length,
            TotalChunks = 2,
            ExpectedSha256 = expectedSha256
        }, _testUserId);

        using (var s1 = new MemoryStream(part1))
        {
            await uploadService.UploadChunkAsync(sessionDto.SessionId, 0, s1, part1.Length, null, _testUserId);
        }

        using (var s2 = new MemoryStream(part2))
        {
            await uploadService.UploadChunkAsync(sessionDto.SessionId, 1, s2, part2.Length, null, _testUserId);
        }

        // Act
        var completedFile = await uploadService.CompleteSessionAsync(sessionDto.SessionId, null, _testUserId);

        // Assert
        Assert.NotNull(completedFile);
        Assert.Equal("greeting.txt", completedFile.Name);
        Assert.Equal("txt", completedFile.Extension);
        Assert.Equal(fullBytes.Length, completedFile.SizeBytes);

        // Check user quota increment
        var user = await db.Users.FindAsync(_testUserId);
        Assert.Equal(fullBytes.Length, user!.UsedQuotaBytes);

        // Check session status is Completed
        var sessionEntity = await db.UploadSessions.FindAsync(sessionDto.SessionId);
        Assert.Equal(UploadSessionStatus.Completed, sessionEntity!.Status);

        // Verify assembled file can be read via file service
        var downloadResult = await fileService.GetFileDownloadStreamAsync(completedFile.Id, _testUserId);
        using var ms = new MemoryStream();
        await downloadResult.Stream!.CopyToAsync(ms);
        Assert.Equal(fullBytes, ms.ToArray());
    }

    [Fact]
    public async Task CompleteSessionAsync_DeduplicatesBlob_WhenSameContentUploaded()
    {
        // Arrange
        using var db = CreateDbContext(allocatedQuota: 50_000_000L, usedQuota: 0);
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var contentBytes = Encoding.UTF8.GetBytes("Deduplication test identical content");
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(contentBytes)).ToLowerInvariant();

        // First session
        var session1 = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "first.txt",
            TotalSizeBytes = contentBytes.Length,
            TotalChunks = 1,
            ExpectedSha256 = expectedSha256
        }, _testUserId);

        using (var s = new MemoryStream(contentBytes))
        {
            await uploadService.UploadChunkAsync(session1.SessionId, 0, s, contentBytes.Length, null, _testUserId);
        }
        var file1 = await uploadService.CompleteSessionAsync(session1.SessionId, null, _testUserId);

        // Second session with same content, different file name
        var session2 = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "second.txt",
            TotalSizeBytes = contentBytes.Length,
            TotalChunks = 1,
            ExpectedSha256 = expectedSha256
        }, _testUserId);

        using (var s = new MemoryStream(contentBytes))
        {
            await uploadService.UploadChunkAsync(session2.SessionId, 0, s, contentBytes.Length, null, _testUserId);
        }
        var file2 = await uploadService.CompleteSessionAsync(session2.SessionId, null, _testUserId);

        // Assert - Both FileItems point to the EXACT same StorageBlob
        Assert.Equal(file1.StorageBlobId, file2.StorageBlobId);

        var blob = await db.StorageBlobs.FindAsync(file1.StorageBlobId);
        Assert.NotNull(blob);
        Assert.Equal(2, blob.ReferenceCount);
    }

    [Fact]
    public async Task GetSessionStatusAsync_ReturnsAccurateProgress()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var sessionDto = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "progress.bin",
            TotalSizeBytes = 100,
            ChunkSizeBytes = 50,
            TotalChunks = 2
        }, _testUserId);

        var chunkBytes = Encoding.UTF8.GetBytes(new string('a', 50));
        using var chunkStream = new MemoryStream(chunkBytes);
        await uploadService.UploadChunkAsync(sessionDto.SessionId, 0, chunkStream, 50, null, _testUserId);

        // Act
        var status = await uploadService.GetSessionStatusAsync(sessionDto.SessionId, _testUserId);

        // Assert
        Assert.Single(status.UploadedChunks);
        Assert.Equal(0, status.UploadedChunks[0]);
        Assert.Single(status.MissingChunks);
        Assert.Equal(1, status.MissingChunks[0]);
        Assert.Equal(50.0, status.ProgressPercentage);
    }

    [Fact]
    public async Task AbortSessionAsync_SetsStatusToAborted_AndRejectsFurtherChunks()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var sessionDto = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "abort_test.bin",
            TotalSizeBytes = 100,
            ChunkSizeBytes = 50,
            TotalChunks = 2
        }, _testUserId);

        // Act
        await uploadService.AbortSessionAsync(sessionDto.SessionId, _testUserId);

        // Assert
        var entity = await db.UploadSessions.FindAsync(sessionDto.SessionId);
        Assert.Equal(UploadSessionStatus.Aborted, entity!.Status);

        using var s = new MemoryStream(Encoding.UTF8.GetBytes("chunk"));
        await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.UploadChunkAsync(sessionDto.SessionId, 0, s, 5, null, _testUserId));
    }

    [Fact]
    public async Task InitSessionAsync_ExactQuotaBoundary_Succeeds()
    {
        // Arrange: 1000 bytes quota, 600 bytes used -> 400 bytes available. Exactly 400 bytes upload
        using var db = CreateDbContext(allocatedQuota: 1000L, usedQuota: 600L);
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var request = new InitUploadSessionRequest
        {
            FileName = "exact_quota.bin",
            TotalSizeBytes = 400L
        };

        // Act
        var session = await uploadService.InitSessionAsync(request, _testUserId);

        // Assert
        Assert.NotNull(session);
        Assert.Equal(400L, session.TotalSizeBytes);
    }

    [Fact]
    public async Task InitSessionAsync_ExceedsQuotaByOneByte_ThrowsValidationException()
    {
        // Arrange: 1000 bytes quota, 600 bytes used -> 400 bytes available. 401 bytes upload
        using var db = CreateDbContext(allocatedQuota: 1000L, usedQuota: 600L);
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var request = new InitUploadSessionRequest
        {
            FileName = "over_quota.bin",
            TotalSizeBytes = 401L
        };

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.InitSessionAsync(request, _testUserId));
    }

    [Fact]
    public async Task UploadChunkAsync_ExpiredSession_ThrowsValidationException()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var sessionDto = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "expired.bin",
            TotalSizeBytes = 100,
            TotalChunks = 1
        }, _testUserId);

        // Manually set ExpiresAt in the past
        var sessionEntity = await db.UploadSessions.FindAsync(sessionDto.SessionId);
        sessionEntity!.ExpiresAt = DateTime.UtcNow.AddMinutes(-10);
        await db.SaveChangesAsync();

        using var s = new MemoryStream(new byte[100]);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.UploadChunkAsync(sessionDto.SessionId, 0, s, 100, null, _testUserId));
        Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadChunkAsync_RetriedChunk_OverwritesCleanly()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var sessionDto = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "retry.bin",
            TotalSizeBytes = 50,
            TotalChunks = 1
        }, _testUserId);

        var chunkDataInitial = Encoding.UTF8.GetBytes("Initial payload");
        using var s1 = new MemoryStream(chunkDataInitial);
        await uploadService.UploadChunkAsync(sessionDto.SessionId, 0, s1, chunkDataInitial.Length, null, _testUserId);

        var chunkDataRetry = Encoding.UTF8.GetBytes("Retry new payload");
        using var s2 = new MemoryStream(chunkDataRetry);

        // Act
        var result = await uploadService.UploadChunkAsync(sessionDto.SessionId, 0, s2, chunkDataRetry.Length, null, _testUserId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(chunkDataRetry.Length, result.SizeBytes);

        var chunksInDb = await db.UploadChunks.Where(c => c.UploadSessionId == sessionDto.SessionId).ToListAsync();
        Assert.Single(chunksInDb);
        Assert.Equal(chunkDataRetry.Length, chunksInDb[0].SizeBytes);
    }

    [Fact]
    public async Task CompleteSessionAsync_SingleChunkSession_Succeeds()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var contentBytes = Encoding.UTF8.GetBytes("Single chunk complete test content");
        var expectedHash = Convert.ToHexString(SHA256.HashData(contentBytes)).ToLowerInvariant();

        var sessionDto = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "single.txt",
            TotalSizeBytes = contentBytes.Length,
            TotalChunks = 1,
            ExpectedSha256 = expectedHash
        }, _testUserId);

        using var s = new MemoryStream(contentBytes);
        await uploadService.UploadChunkAsync(sessionDto.SessionId, 0, s, contentBytes.Length, null, _testUserId);

        // Act
        var completed = await uploadService.CompleteSessionAsync(sessionDto.SessionId, null, _testUserId);

        // Assert
        Assert.NotNull(completed);
        Assert.Equal("single.txt", completed.Name);
        Assert.Equal(contentBytes.Length, completed.SizeBytes);
    }

    [Fact]
    public async Task DirectStreamUploadAsync_UnknownSizeBytes_UploadsSuccessfully()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var contentBytes = Encoding.UTF8.GetBytes("Dynamic chunked stream without upfront content length");
        using var contentStream = new MemoryStream(contentBytes);

        var request = new UploadFileStreamRequest
        {
            Content = contentStream,
            FileName = "unknown_size.txt",
            ContentType = "text/plain",
            SizeBytes = null // Unknown upfront size
        };

        // Act
        var result = await uploadService.DirectStreamUploadAsync(request, _testUserId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("unknown_size.txt", result.Name);
        Assert.Equal(contentBytes.Length, result.SizeBytes);

        var user = await db.Users.FindAsync(_testUserId);
        Assert.Equal(contentBytes.Length, user!.UsedQuotaBytes);
    }

    [Fact]
    public async Task DirectStreamUploadAsync_UnknownSizeBytes_ExceedsQuotaAfterWrite_CleansUpAndThrows()
    {
        // Arrange (1000 quota, 900 used -> 100 available)
        using var db = CreateDbContext(allocatedQuota: 1000L, usedQuota: 900L);
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var contentBytes = Encoding.UTF8.GetBytes(new string('x', 200)); // 200 > 100
        using var contentStream = new MemoryStream(contentBytes);

        var request = new UploadFileStreamRequest
        {
            Content = contentStream,
            FileName = "overquota_stream.bin",
            SizeBytes = null
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.DirectStreamUploadAsync(request, _testUserId));
        Assert.Contains("quota exceeded", ex.Message, StringComparison.OrdinalIgnoreCase);

        var user = await db.Users.FindAsync(_testUserId);
        Assert.Equal(900L, user!.UsedQuotaBytes); // Quota not consumed
    }

    [Fact]
    public async Task InitSessionAsync_ZeroOrNegativeTotalChunks_ThrowsValidationException()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.InitSessionAsync(new InitUploadSessionRequest
            {
                FileName = "test.bin",
                TotalSizeBytes = 100,
                TotalChunks = 0
            }, _testUserId));

        await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.InitSessionAsync(new InitUploadSessionRequest
            {
                FileName = "test.bin",
                TotalSizeBytes = 100,
                TotalChunks = -5
            }, _testUserId));
    }

    [Fact]
    public async Task InitSessionAsync_ZeroOrNegativeChunkSize_ThrowsValidationException()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.InitSessionAsync(new InitUploadSessionRequest
            {
                FileName = "test.bin",
                TotalSizeBytes = 100,
                ChunkSizeBytes = 0
            }, _testUserId));

        await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.InitSessionAsync(new InitUploadSessionRequest
            {
                FileName = "test.bin",
                TotalSizeBytes = 100,
                ChunkSizeBytes = -10
            }, _testUserId));
    }

    [Fact]
    public async Task InitSessionAsync_ExceedsMaxChunksLimit_ThrowsValidationException()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.InitSessionAsync(new InitUploadSessionRequest
            {
                FileName = "huge.bin",
                TotalSizeBytes = 100_000_000,
                TotalChunks = 60_000
            }, _testUserId));

        Assert.Contains("50,000", ex.Message);
    }

    [Fact]
    public async Task CompleteSessionAsync_WhenAssemblyFails_RevertsStatusToUploading()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var contentBytes = Encoding.UTF8.GetBytes("Corrupted session data");
        var badExpectedSha256 = "0000000000000000000000000000000000000000000000000000000000000000";

        var sessionDto = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "assembly_fail.bin",
            TotalSizeBytes = contentBytes.Length,
            TotalChunks = 1,
            ExpectedSha256 = badExpectedSha256
        }, _testUserId);

        using var s = new MemoryStream(contentBytes);
        await uploadService.UploadChunkAsync(sessionDto.SessionId, 0, s, contentBytes.Length, null, _testUserId);

        // Act & Assert - Complete should fail checksum check in AssembleChunksAsync
        await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.CompleteSessionAsync(sessionDto.SessionId, null, _testUserId));

        // Status MUST NOT be stuck in Assembling! It should revert to Uploading.
        var sessionEntity = await db.UploadSessions.FindAsync(sessionDto.SessionId);
        Assert.Equal(UploadSessionStatus.Uploading, sessionEntity!.Status);
    }

    [Fact]
    public async Task CompleteSessionAsync_TargetFolderDeletedDuringSession_ThrowsNotFoundException()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = _testUserId,
            Name = "ActiveFolder",
            IsDeleted = false
        };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        var contentBytes = Encoding.UTF8.GetBytes("Folder file content");

        var sessionDto = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "in_folder.txt",
            TotalSizeBytes = contentBytes.Length,
            TotalChunks = 1,
            FolderId = folder.Id
        }, _testUserId);

        using var s = new MemoryStream(contentBytes);
        await uploadService.UploadChunkAsync(sessionDto.SessionId, 0, s, contentBytes.Length, null, _testUserId);

        // Delete the folder before completing!
        folder.IsDeleted = true;
        folder.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            uploadService.CompleteSessionAsync(sessionDto.SessionId, null, _testUserId));
    }

    [Fact]
    public async Task CompleteSessionAsync_InvalidExpectedSha256Format_ThrowsValidationException()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var contentBytes = Encoding.UTF8.GetBytes("Data");
        var sessionDto = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "badsha.txt",
            TotalSizeBytes = contentBytes.Length,
            TotalChunks = 1
        }, _testUserId);

        using var s = new MemoryStream(contentBytes);
        await uploadService.UploadChunkAsync(sessionDto.SessionId, 0, s, contentBytes.Length, null, _testUserId);

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.CompleteSessionAsync(sessionDto.SessionId, new CompleteUploadSessionRequest
            {
                ExpectedSha256 = "not-a-valid-sha-256"
            }, _testUserId));
    }

    [Fact]
    public async Task CompleteSessionAsync_ConflictingExpectedSha256_ThrowsValidationException()
    {
        // Arrange
        using var db = CreateDbContext();
        var fileService = new FileService(db, _resolver);
        var uploadService = new UploadService(db, fileService, _resolver);

        var contentBytes = Encoding.UTF8.GetBytes("Data");
        var validHashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var validHashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        var sessionDto = await uploadService.InitSessionAsync(new InitUploadSessionRequest
        {
            FileName = "conflict.txt",
            TotalSizeBytes = contentBytes.Length,
            TotalChunks = 1,
            ExpectedSha256 = validHashA
        }, _testUserId);

        using var s = new MemoryStream(contentBytes);
        await uploadService.UploadChunkAsync(sessionDto.SessionId, 0, s, contentBytes.Length, null, _testUserId);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            uploadService.CompleteSessionAsync(sessionDto.SessionId, new CompleteUploadSessionRequest
            {
                ExpectedSha256 = validHashB
            }, _testUserId));

        Assert.Contains("does not match", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
