using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Omega.StorageNode.Services;

namespace Omega.StorageNode.Tests;

public class StorageServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly StorageService _storageService;

    public StorageServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "StorageServiceTest_" + Guid.NewGuid().ToString("N"));
        _storageService = new StorageService(_testDir, NullLogger<StorageService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task WriteChunkAsync_ValidContent_WritesAndComputesHash()
    {
        var sessionId = Guid.NewGuid();
        var chunkIndex = 0;
        var bytes = Encoding.UTF8.GetBytes("Hello World from chunk 0!");
        using var stream = new MemoryStream(bytes);

        var expectedHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var result = await _storageService.WriteChunkAsync(sessionId, chunkIndex, stream, expectedHash);

        Assert.Equal(chunkIndex, result.ChunkIndex);
        Assert.Equal(bytes.Length, result.SizeBytes);
        Assert.Equal(expectedHash, result.Sha256Hash);

        var chunkPath = Path.Combine(_testDir, "temp", "sessions", sessionId.ToString("N"), "0.part");
        Assert.True(File.Exists(chunkPath));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(chunkPath));
    }

    [Fact]
    public async Task WriteChunkAsync_MismatchedSha256_ThrowsAndDeletesTemp()
    {
        var sessionId = Guid.NewGuid();
        var bytes = Encoding.UTF8.GetBytes("Chunk data");
        using var stream = new MemoryStream(bytes);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _storageService.WriteChunkAsync(sessionId, 0, stream, "wrong_hash_1234567890abcdef"));

        var sessionDir = Path.Combine(_testDir, "temp", "sessions", sessionId.ToString("N"));
        var tempPart = Path.Combine(sessionDir, "0.part.tmp");
        var part = Path.Combine(sessionDir, "0.part");

        Assert.False(File.Exists(tempPart));
        Assert.False(File.Exists(part));
    }

    [Fact]
    public async Task WriteChunkAsync_EmptyStream_ThrowsArgumentException()
    {
        var sessionId = Guid.NewGuid();
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storageService.WriteChunkAsync(sessionId, 0, stream));
    }

    [Fact]
    public async Task AssembleChunksAsync_ValidChunks_AssemblesIntoShardedBlob()
    {
        var sessionId = Guid.NewGuid();
        var chunk0 = Encoding.UTF8.GetBytes("Part 1: The Beginning. ");
        var chunk1 = Encoding.UTF8.GetBytes("Part 2: The Middle. ");
        var chunk2 = Encoding.UTF8.GetBytes("Part 3: The End.");

        using (var s0 = new MemoryStream(chunk0))
            await _storageService.WriteChunkAsync(sessionId, 0, s0);
        using (var s1 = new MemoryStream(chunk1))
            await _storageService.WriteChunkAsync(sessionId, 1, s1);
        using (var s2 = new MemoryStream(chunk2))
            await _storageService.WriteChunkAsync(sessionId, 2, s2);

        var fullContent = new List<byte>();
        fullContent.AddRange(chunk0);
        fullContent.AddRange(chunk1);
        fullContent.AddRange(chunk2);
        var fullBytes = fullContent.ToArray();

        var expectedSha256 = Convert.ToHexString(SHA256.HashData(fullBytes)).ToLowerInvariant();
        var assignedBlobId = Guid.NewGuid();

        var blobResult = await _storageService.AssembleChunksAsync(sessionId, 3, expectedSha256, assignedBlobId);

        Assert.Equal(assignedBlobId, blobResult.BlobId);
        Assert.Equal(expectedSha256, blobResult.Sha256Hash);
        Assert.Equal(fullBytes.Length, blobResult.SizeBytes);

        // Verify sharded path: blobs/{sha[0..2]}/{sha[2..4]}/{blobId}.blob
        var shard1 = expectedSha256[..2];
        var shard2 = expectedSha256.Substring(2, 2);
        var expectedRelativePath = $"blobs/{shard1}/{shard2}/{assignedBlobId}.blob";
        Assert.Equal(expectedRelativePath, blobResult.StorageRelativePath);

        var fullPath = Path.Combine(_testDir, "blobs", shard1, shard2, $"{assignedBlobId}.blob");
        Assert.True(File.Exists(fullPath));
        Assert.Equal(fullBytes, await File.ReadAllBytesAsync(fullPath));

        // Verify session chunk folder was removed
        var sessionDir = Path.Combine(_testDir, "temp", "sessions", sessionId.ToString("N"));
        Assert.False(Directory.Exists(sessionDir));
    }

    [Fact]
    public async Task AssembleChunksAsync_MissingChunk_ThrowsFileNotFoundException()
    {
        var sessionId = Guid.NewGuid();
        var chunk0 = Encoding.UTF8.GetBytes("Part 0 only.");
        using (var s0 = new MemoryStream(chunk0))
            await _storageService.WriteChunkAsync(sessionId, 0, s0);

        // Expects 2 chunks, but only index 0 uploaded
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _storageService.AssembleChunksAsync(sessionId, 2));
    }

    [Fact]
    public async Task AssembleChunksAsync_MismatchedSha256_ThrowsAndCleansTemp()
    {
        var sessionId = Guid.NewGuid();
        var chunk0 = Encoding.UTF8.GetBytes("Some content.");
        using (var s0 = new MemoryStream(chunk0))
            await _storageService.WriteChunkAsync(sessionId, 0, s0);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _storageService.AssembleChunksAsync(sessionId, 1, "mismatched_sha256_hex"));

        var assembleTemp = Path.Combine(_testDir, "temp", $"assemble_{sessionId:N}.tmp");
        Assert.False(File.Exists(assembleTemp));
    }

    [Fact]
    public async Task WriteBlobDirectAsync_ValidStream_WritesAndLocatesBlob()
    {
        var blobId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("Direct blob write stream content 12345");
        using var stream = new MemoryStream(content);
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        var result = await _storageService.WriteBlobDirectAsync(blobId, stream, expectedSha256, content.Length);

        Assert.Equal(blobId, result.BlobId);
        Assert.Equal(expectedSha256, result.Sha256Hash);
        Assert.Equal(content.Length, result.SizeBytes);

        // Read back
        var readResult = await _storageService.OpenReadBlobAsync(blobId);
        Assert.NotNull(readResult);
        Assert.Equal(content.Length, readResult.Value.Length);

        using var memory = new MemoryStream();
        await readResult.Value.Stream.CopyToAsync(memory);
        Assert.Equal(content, memory.ToArray());
    }

    [Fact]
    public async Task DeleteBlobAsync_ExistingBlob_DeletesSuccessfully()
    {
        var blobId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("Content to delete");
        using var stream = new MemoryStream(content);

        await _storageService.WriteBlobDirectAsync(blobId, stream);

        var readBefore = await _storageService.OpenReadBlobAsync(blobId);
        Assert.NotNull(readBefore);
        readBefore.Value.Stream.Dispose();

        var deleted = await _storageService.DeleteBlobAsync(blobId);
        Assert.True(deleted);

        var readAfter = await _storageService.OpenReadBlobAsync(blobId);
        Assert.Null(readAfter);
    }

    [Fact]
    public async Task DeleteSessionTempDataAsync_DeletesDirectory()
    {
        var sessionId = Guid.NewGuid();
        var chunk = Encoding.UTF8.GetBytes("Temp chunk");
        using (var s = new MemoryStream(chunk))
            await _storageService.WriteChunkAsync(sessionId, 0, s);

        var sessionDir = Path.Combine(_testDir, "temp", "sessions", sessionId.ToString("N"));
        Assert.True(Directory.Exists(sessionDir));

        await _storageService.DeleteSessionTempDataAsync(sessionId);
        Assert.False(Directory.Exists(sessionDir));
    }

    [Fact]
    public async Task CleanupExpiredTempDataAsync_CleansOnlyExpiredItems()
    {
        var expiredSessionDir = Path.Combine(_testDir, "temp", "sessions", "expired_session");
        Directory.CreateDirectory(expiredSessionDir);
        File.WriteAllText(Path.Combine(expiredSessionDir, "0.part"), "data");
        Directory.SetLastWriteTimeUtc(expiredSessionDir, DateTime.UtcNow.AddDays(-2));

        var freshSessionDir = Path.Combine(_testDir, "temp", "sessions", "fresh_session");
        Directory.CreateDirectory(freshSessionDir);
        File.WriteAllText(Path.Combine(freshSessionDir, "0.part"), "data");
        Directory.SetLastWriteTimeUtc(freshSessionDir, DateTime.UtcNow);

        var expiredTmpFile = Path.Combine(_testDir, "temp", "assemble_old.tmp");
        File.WriteAllText(expiredTmpFile, "old data");
        File.SetLastWriteTimeUtc(expiredTmpFile, DateTime.UtcNow.AddDays(-2));

        var freshTmpFile = Path.Combine(_testDir, "temp", "assemble_new.tmp");
        File.WriteAllText(freshTmpFile, "fresh data");
        File.SetLastWriteTimeUtc(freshTmpFile, DateTime.UtcNow);

        var (deletedSessions, deletedFiles) = await _storageService.CleanupExpiredTempDataAsync(TimeSpan.FromHours(24));

        Assert.Equal(1, deletedSessions);
        Assert.Equal(1, deletedFiles);

        Assert.False(Directory.Exists(expiredSessionDir));
        Assert.True(Directory.Exists(freshSessionDir));

        Assert.False(File.Exists(expiredTmpFile));
        Assert.True(File.Exists(freshTmpFile));
    }

    [Fact]
    public void GetDiskTelemetry_ReturnsTelemetry()
    {
        var telemetry = _storageService.GetDiskTelemetry();
        Assert.NotNull(telemetry.DiskStatus);
    }
}
