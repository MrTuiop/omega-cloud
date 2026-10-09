using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Infrastructure.Storage;

namespace OmegaCloud.Tests.Storage;

public class LocalDiskStorageDriverTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly LocalDiskStorageDriver _driver;

    public LocalDiskStorageDriverTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "StorageDriverTests_" + Guid.NewGuid().ToString("N"));
        _driver = new LocalDiskStorageDriver(_tempDirectory, NullLogger<LocalDiskStorageDriver>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    [Fact]
    public async Task WriteAsync_CreatesShardedFile_WithCorrectPathAndSha256()
    {
        // Arrange
        var content = "Omega Cloud - The next generation private cloud storage";
        var contentBytes = Encoding.UTF8.GetBytes(content);
        using var stream = new MemoryStream(contentBytes);
        var expectedHash = Convert.ToHexString(SHA256.HashData(contentBytes)).ToLowerInvariant();

        // Act
        var result = await _driver.WriteAsync(stream, new StorageWriteOptions());

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedHash, result.Sha256Hash);
        Assert.Equal(contentBytes.Length, result.SizeBytes);

        var shard1 = expectedHash[..2];
        var shard2 = expectedHash.Substring(2, 2);
        var expectedRelativePath = $"blobs/{shard1}/{shard2}/{result.BlobId}.blob";
        Assert.Equal(expectedRelativePath, result.StorageRelativePath);

        var physicalPath = Path.Combine(_tempDirectory, "blobs", shard1, shard2, $"{result.BlobId}.blob");
        Assert.True(File.Exists(physicalPath));

        var savedBytes = await File.ReadAllBytesAsync(physicalPath);
        Assert.Equal(contentBytes, savedBytes);
    }

    [Fact]
    public async Task WriteAsync_WithExpectedSha256_ValidatesCorrectly()
    {
        // Arrange
        var contentBytes = Encoding.UTF8.GetBytes("Valid checksum stream");
        using var stream = new MemoryStream(contentBytes);
        var correctHash = Convert.ToHexString(SHA256.HashData(contentBytes)).ToLowerInvariant();

        // Act
        var result = await _driver.WriteAsync(stream, new StorageWriteOptions
        {
            ExpectedSha256 = correctHash
        });

        // Assert
        Assert.Equal(correctHash, result.Sha256Hash);
    }

    [Fact]
    public async Task WriteAsync_WithMismatchedSha256_ThrowsValidationException()
    {
        // Arrange
        var contentBytes = Encoding.UTF8.GetBytes("Valid content");
        using var stream = new MemoryStream(contentBytes);

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            _driver.WriteAsync(stream, new StorageWriteOptions
            {
                ExpectedSha256 = "0000000000000000000000000000000000000000000000000000000000000000"
            }));
    }

    [Fact]
    public async Task WriteAsync_WithMismatchedExpectedSize_ThrowsValidationException()
    {
        // Arrange
        var contentBytes = Encoding.UTF8.GetBytes("Size check");
        using var stream = new MemoryStream(contentBytes);

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            _driver.WriteAsync(stream, new StorageWriteOptions
            {
                ExpectedSize = 999999
            }));
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsReadableStream_WithMatchingContent()
    {
        // Arrange
        var contentBytes = Encoding.UTF8.GetBytes("Streaming read verification");
        using var writeStream = new MemoryStream(contentBytes);
        var writeResult = await _driver.WriteAsync(writeStream, new StorageWriteOptions());

        var blob = new StorageBlob
        {
            Id = writeResult.BlobId,
            StorageRelativePath = writeResult.StorageRelativePath,
            SizeBytes = writeResult.SizeBytes
        };

        // Act
        using var readStream = await _driver.OpenReadAsync(blob);
        using var ms = new MemoryStream();
        await readStream.CopyToAsync(ms);

        // Assert
        Assert.Equal(contentBytes, ms.ToArray());
    }

    [Fact]
    public async Task OpenReadAsync_NonExistentBlob_ThrowsNotFoundException()
    {
        // Arrange
        var blob = new StorageBlob
        {
            Id = Guid.NewGuid(),
            StorageRelativePath = "blobs/ab/cd/nonexistent.blob"
        };

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _driver.OpenReadAsync(blob));
    }

    [Fact]
    public async Task OpenReadAsync_PathTraversalAttempt_ThrowsValidationException()
    {
        // Arrange
        var blob = new StorageBlob
        {
            Id = Guid.NewGuid(),
            StorageRelativePath = "../../Windows/System32/cmd.exe"
        };

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() => _driver.OpenReadAsync(blob));
    }

    [Fact]
    public async Task DeleteAsync_DeletesPhysicalFile_AndCleansEmptyShards()
    {
        // Arrange
        var contentBytes = Encoding.UTF8.GetBytes("Delete test");
        using var stream = new MemoryStream(contentBytes);
        var writeResult = await _driver.WriteAsync(stream, new StorageWriteOptions());

        var blob = new StorageBlob
        {
            Id = writeResult.BlobId,
            StorageRelativePath = writeResult.StorageRelativePath
        };

        var physicalPath = Path.Combine(_tempDirectory, writeResult.StorageRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(physicalPath));

        // Act
        await _driver.DeleteAsync(blob);

        // Assert
        Assert.False(File.Exists(physicalPath));
    }

    [Fact]
    public async Task PingAsync_ReturnsTrue()
    {
        // Act
        var result = await _driver.PingAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task WriteChunkAsync_SavesChunkFile_WithCorrectSha256()
    {
        // Arrange
        var session = new UploadSession
        {
            Id = Guid.NewGuid(),
            TotalChunks = 2,
            TotalSizeBytes = 100
        };
        var chunkBytes = Encoding.UTF8.GetBytes("Chunk 0 data content");
        using var stream = new MemoryStream(chunkBytes);
        var expectedHash = Convert.ToHexString(SHA256.HashData(chunkBytes)).ToLowerInvariant();

        // Act
        var result = await _driver.WriteChunkAsync(session, 0, stream);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.ChunkIndex);
        Assert.Equal(chunkBytes.Length, result.SizeBytes);
        Assert.Equal(expectedHash, result.Sha256Hash);

        var chunkFile = Path.Combine(_tempDirectory, "temp", "sessions", session.Id.ToString("N"), "0.part");
        Assert.True(File.Exists(chunkFile));
        var savedBytes = await File.ReadAllBytesAsync(chunkFile);
        Assert.Equal(chunkBytes, savedBytes);
    }

    [Fact]
    public async Task AssembleChunksAsync_ConcatenatesAllChunks_InOrder_AndCleansUpTemp()
    {
        // Arrange
        var chunk0Bytes = Encoding.UTF8.GetBytes("Part 1: Hello ");
        var chunk1Bytes = Encoding.UTF8.GetBytes("Part 2: World!");
        var combinedBytes = Encoding.UTF8.GetBytes("Part 1: Hello Part 2: World!");
        var expectedHash = Convert.ToHexString(SHA256.HashData(combinedBytes)).ToLowerInvariant();

        var session = new UploadSession
        {
            Id = Guid.NewGuid(),
            TotalChunks = 2,
            TotalSizeBytes = combinedBytes.Length,
            ExpectedSha256 = expectedHash
        };

        using var stream0 = new MemoryStream(chunk0Bytes);
        using var stream1 = new MemoryStream(chunk1Bytes);

        var res0 = await _driver.WriteChunkAsync(session, 0, stream0);
        var res1 = await _driver.WriteChunkAsync(session, 1, stream1);

        var chunks = new List<UploadChunk>
        {
            new UploadChunk { ChunkIndex = 0, SizeBytes = res0.SizeBytes, Sha256Hash = res0.Sha256Hash },
            new UploadChunk { ChunkIndex = 1, SizeBytes = res1.SizeBytes, Sha256Hash = res1.Sha256Hash }
        };

        // Act
        var blobResult = await _driver.AssembleChunksAsync(session, chunks);

        // Assert
        Assert.NotNull(blobResult);
        Assert.Equal(expectedHash, blobResult.Sha256Hash);
        Assert.Equal(combinedBytes.Length, blobResult.SizeBytes);

        var physicalPath = Path.Combine(_tempDirectory, blobResult.StorageRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(physicalPath));
        var assembledBytes = await File.ReadAllBytesAsync(physicalPath);
        Assert.Equal(combinedBytes, assembledBytes);

        // Temp session dir should have been cleaned up
        var sessionDir = Path.Combine(_tempDirectory, "temp", "sessions", session.Id.ToString("N"));
        Assert.False(Directory.Exists(sessionDir));
    }

    [Fact]
    public async Task AssembleChunksAsync_WithMismatchedExpectedSha256_ThrowsValidationException()
    {
        // Arrange
        var chunkBytes = Encoding.UTF8.GetBytes("Single chunk content");
        var session = new UploadSession
        {
            Id = Guid.NewGuid(),
            TotalChunks = 1,
            TotalSizeBytes = chunkBytes.Length,
            ExpectedSha256 = "0000000000000000000000000000000000000000000000000000000000000000"
        };

        using var stream = new MemoryStream(chunkBytes);
        var res = await _driver.WriteChunkAsync(session, 0, stream);
        var chunks = new List<UploadChunk>
        {
            new UploadChunk { ChunkIndex = 0, SizeBytes = res.SizeBytes, Sha256Hash = res.Sha256Hash }
        };

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() => _driver.AssembleChunksAsync(session, chunks));
    }

    [Fact]
    public async Task AssembleChunksAsync_WithMissingChunk_ThrowsValidationException()
    {
        // Arrange
        var session = new UploadSession
        {
            Id = Guid.NewGuid(),
            TotalChunks = 2,
            TotalSizeBytes = 100
        };

        var chunk0Bytes = Encoding.UTF8.GetBytes("Chunk 0 only");
        using var stream = new MemoryStream(chunk0Bytes);
        var res0 = await _driver.WriteChunkAsync(session, 0, stream);

        var chunks = new List<UploadChunk>
        {
            new UploadChunk { ChunkIndex = 0, SizeBytes = res0.SizeBytes, Sha256Hash = res0.Sha256Hash }
        };

        // Act & Assert (chunk 1 is missing)
        await Assert.ThrowsAsync<ValidationException>(() => _driver.AssembleChunksAsync(session, chunks));
    }

    [Fact]
    public async Task DeleteSessionTempDataAsync_DeletesSessionDirectory()
    {
        // Arrange
        var session = new UploadSession
        {
            Id = Guid.NewGuid(),
            TotalChunks = 1,
            TotalSizeBytes = 50
        };

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Temp data"));
        await _driver.WriteChunkAsync(session, 0, stream);

        var sessionDir = Path.Combine(_tempDirectory, "temp", "sessions", session.Id.ToString("N"));
        Assert.True(Directory.Exists(sessionDir));

        // Act
        await _driver.DeleteSessionTempDataAsync(session);

        // Assert
        Assert.False(Directory.Exists(sessionDir));
    }

    [Fact]
    public async Task WriteChunkAsync_WithMismatchedExpectedSha256_ThrowsAndDoesNotLeaveChunkFile()
    {
        // Arrange
        var session = new UploadSession
        {
            Id = Guid.NewGuid(),
            TotalChunks = 1,
            TotalSizeBytes = 50
        };

        var chunkBytes = Encoding.UTF8.GetBytes("Chunk test content");
        using var stream = new MemoryStream(chunkBytes);

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            _driver.WriteChunkAsync(session, 0, stream, "0000000000000000000000000000000000000000000000000000000000000000"));

        var chunkFile = Path.Combine(_tempDirectory, "temp", "sessions", session.Id.ToString("N"), "0.part");
        Assert.False(File.Exists(chunkFile));
    }

    [Fact]
    public async Task WriteChunkAsync_EmptyStream_ThrowsValidationExceptionAndCleansUp()
    {
        // Arrange
        var session = new UploadSession
        {
            Id = Guid.NewGuid(),
            TotalChunks = 1,
            TotalSizeBytes = 0
        };

        using var stream = new MemoryStream(Array.Empty<byte>());

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _driver.WriteChunkAsync(session, 0, stream));
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);

        var chunkFile = Path.Combine(_tempDirectory, "temp", "sessions", session.Id.ToString("N"), "0.part");
        Assert.False(File.Exists(chunkFile));
    }
}
