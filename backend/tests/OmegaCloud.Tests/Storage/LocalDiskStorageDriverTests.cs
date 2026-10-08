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
}
