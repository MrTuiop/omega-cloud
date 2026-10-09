using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Omega.StorageNode.Configuration;
using Omega.StorageNode.Models;

namespace Omega.StorageNode.Services;

public interface IStorageService
{
    Task<UploadChunkResult> WriteChunkAsync(
        Guid sessionId,
        int chunkIndex,
        Stream chunkStream,
        string? expectedSha256 = null,
        CancellationToken ct = default);

    Task<StorageBlobResult> AssembleChunksAsync(
        Guid sessionId,
        int totalChunks,
        string? expectedSha256 = null,
        Guid? blobId = null,
        CancellationToken ct = default);

    Task<StorageBlobResult> WriteBlobDirectAsync(
        Guid blobId,
        Stream contentStream,
        string? expectedSha256 = null,
        long? expectedSize = null,
        CancellationToken ct = default);

    Task<(Stream Stream, long Length, string ContentType)?> OpenReadBlobAsync(
        Guid blobId,
        CancellationToken ct = default);

    Task<bool> DeleteBlobAsync(
        Guid blobId,
        CancellationToken ct = default);

    Task DeleteSessionTempDataAsync(
        Guid sessionId,
        CancellationToken ct = default);

    Task<(int DeletedSessions, int DeletedFiles)> CleanupExpiredTempDataAsync(
        TimeSpan expiration,
        CancellationToken ct = default);

    (long TotalBytes, long AvailableBytes, string DiskStatus) GetDiskTelemetry();
    double? GetCpuUsage();
}

public class StorageService : IStorageService
{
    private readonly string _storageRoot;
    private readonly ILogger<StorageService> _logger;
    private readonly ConcurrentDictionary<Guid, string> _blobPathIndex = new();
    private readonly ConcurrentDictionary<string, object> _chunkMoveLocks = new();

    private const int BufferSize = 81920; // 80 KB standard buffer

    public StorageService(
        IOptions<StorageNodeOptions> options,
        ILogger<StorageService> logger)
    {
        _logger = logger;
        _storageRoot = Path.GetFullPath(options.Value.StorageRootPath);

        EnsureDirectoryExists(_storageRoot);
        EnsureDirectoryExists(Path.Combine(_storageRoot, "temp"));
        EnsureDirectoryExists(Path.Combine(_storageRoot, "temp", "sessions"));
        EnsureDirectoryExists(Path.Combine(_storageRoot, "blobs"));
    }

    // Direct path constructor (ideal for testing)
    public StorageService(
        string storageRoot,
        ILogger<StorageService> logger)
    {
        _logger = logger;
        _storageRoot = Path.GetFullPath(storageRoot);

        EnsureDirectoryExists(_storageRoot);
        EnsureDirectoryExists(Path.Combine(_storageRoot, "temp"));
        EnsureDirectoryExists(Path.Combine(_storageRoot, "temp", "sessions"));
        EnsureDirectoryExists(Path.Combine(_storageRoot, "blobs"));
    }

    public async Task<UploadChunkResult> WriteChunkAsync(
        Guid sessionId,
        int chunkIndex,
        Stream chunkStream,
        string? expectedSha256 = null,
        CancellationToken ct = default)
    {
        var sessionDir = Path.Combine(_storageRoot, "temp", "sessions", sessionId.ToString("N"));
        EnsureDirectoryExists(sessionDir);

        var tempFilePath = Path.Combine(sessionDir, $"{chunkIndex}_{Guid.NewGuid():N}.part.tmp");
        var chunkFilePath = Path.Combine(sessionDir, $"{chunkIndex}.part");

        long totalBytes = 0;
        string hashHex;

        try
        {
            await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            using (var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[BufferSize];
                int bytesRead;

                while ((bytesRead = await chunkStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                    incrementalHash.AppendData(buffer, 0, bytesRead);
                    totalBytes += bytesRead;
                }

                await fileStream.FlushAsync(ct);
                var hashBytes = incrementalHash.GetHashAndReset();
                hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            if (totalBytes <= 0)
            {
                TryDeleteFile(tempFilePath);
                throw new ArgumentException("Chunk content cannot be empty.");
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                var expected = expectedSha256.Trim().ToLowerInvariant();
                if (!string.Equals(hashHex, expected, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(tempFilePath);
                    throw new InvalidOperationException($"Chunk SHA-256 mismatch. Expected: {expected}, Computed: {hashHex}");
                }
            }

            var lockKey = $"{sessionId:N}_{chunkIndex}";
            var moveLock = _chunkMoveLocks.GetOrAdd(lockKey, static _ => new object());
            lock (moveLock)
            {
                MoveWithRetry(tempFilePath, chunkFilePath);
            }
            _chunkMoveLocks.TryRemove(lockKey, out _);

            _logger.LogDebug("Chunk {ChunkIndex} written for session {SessionId} ({SizeBytes} bytes, SHA-256: {Hash})",
                chunkIndex, sessionId, totalBytes, hashHex);

            return new UploadChunkResult
            {
                ChunkIndex = chunkIndex,
                SizeBytes = totalBytes,
                Sha256Hash = hashHex
            };
        }
        catch (Exception ex)
        {
            TryDeleteFile(tempFilePath);
            if (ex is not ArgumentException && ex is not InvalidOperationException)
            {
                _logger.LogError(ex, "Failed to write chunk {ChunkIndex} for session {SessionId}", chunkIndex, sessionId);
            }
            throw;
        }
    }

    public async Task<StorageBlobResult> AssembleChunksAsync(
        Guid sessionId,
        int totalChunks,
        string? expectedSha256 = null,
        Guid? blobId = null,
        CancellationToken ct = default)
    {
        var sessionDir = Path.Combine(_storageRoot, "temp", "sessions", sessionId.ToString("N"));
        if (!Directory.Exists(sessionDir))
        {
            throw new DirectoryNotFoundException($"Session temp directory not found for session '{sessionId}'.");
        }

        if (totalChunks <= 0)
        {
            throw new ArgumentException($"Total chunks must be greater than zero. Received: {totalChunks}.");
        }

        for (int i = 0; i < totalChunks; i++)
        {
            var partPath = Path.Combine(sessionDir, $"{i}.part");
            if (!File.Exists(partPath))
            {
                throw new FileNotFoundException($"Missing chunk file '{i}.part' for session '{sessionId}'.");
            }
        }

        var tempDir = Path.Combine(_storageRoot, "temp");
        EnsureDirectoryExists(tempDir);
        var assembleTempPath = Path.Combine(tempDir, $"assemble_{sessionId:N}_{Guid.NewGuid():N}.tmp");

        long totalBytes = 0;
        string hashHex;

        try
        {
            await using (var destStream = new FileStream(assembleTempPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            using (var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[BufferSize];

                for (int i = 0; i < totalChunks; i++)
                {
                    var partPath = Path.Combine(sessionDir, $"{i}.part");
                    await using var chunkStream = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);

                    int bytesRead;
                    while ((bytesRead = await chunkStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await destStream.WriteAsync(buffer, 0, bytesRead, ct);
                        incrementalHash.AppendData(buffer, 0, bytesRead);
                        totalBytes += bytesRead;
                    }
                }

                await destStream.FlushAsync(ct);
                var hashBytes = incrementalHash.GetHashAndReset();
                hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                var expected = expectedSha256.Trim().ToLowerInvariant();
                if (!string.Equals(hashHex, expected, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(assembleTempPath);
                    throw new InvalidOperationException($"Checksum mismatch. Expected SHA-256: {expected}, Computed: {hashHex}");
                }
            }

            var finalBlobId = blobId ?? Guid.NewGuid();
            var shard1 = hashHex[..2];
            var shard2 = hashHex.Substring(2, 2);
            var relativePath = $"blobs/{shard1}/{shard2}/{finalBlobId}.blob";
            var targetDirectory = Path.Combine(_storageRoot, "blobs", shard1, shard2);
            var finalFilePath = Path.Combine(targetDirectory, $"{finalBlobId}.blob");

            EnsureDirectoryExists(targetDirectory);

            MoveWithRetry(assembleTempPath, finalFilePath);

            // Clean session chunks directory
            TryDeleteDirectory(sessionDir);

            _blobPathIndex[finalBlobId] = finalFilePath;

            _logger.LogInformation("Assembled {TotalChunks} chunks into blob {BlobId} at {RelativePath} ({SizeBytes} bytes)",
                totalChunks, finalBlobId, relativePath, totalBytes);

            return new StorageBlobResult
            {
                BlobId = finalBlobId,
                Sha256Hash = hashHex,
                SizeBytes = totalBytes,
                StorageRelativePath = relativePath
            };
        }
        catch (Exception ex)
        {
            TryDeleteFile(assembleTempPath);
            if (ex is not DirectoryNotFoundException && ex is not FileNotFoundException && ex is not InvalidOperationException)
            {
                _logger.LogError(ex, "Failed to assemble chunks for session {SessionId}", sessionId);
            }
            throw;
        }
    }

    public async Task<StorageBlobResult> WriteBlobDirectAsync(
        Guid blobId,
        Stream contentStream,
        string? expectedSha256 = null,
        long? expectedSize = null,
        CancellationToken ct = default)
    {
        var tempDir = Path.Combine(_storageRoot, "temp");
        EnsureDirectoryExists(tempDir);
        var tempFilePath = Path.Combine(tempDir, $"upload_{Guid.NewGuid():N}.tmp");

        long totalBytes = 0;
        string hashHex;

        try
        {
            await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            using (var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[BufferSize];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                    incrementalHash.AppendData(buffer, 0, bytesRead);
                    totalBytes += bytesRead;
                }

                await fileStream.FlushAsync(ct);
                var hashBytes = incrementalHash.GetHashAndReset();
                hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                var expected = expectedSha256.Trim().ToLowerInvariant();
                if (!string.Equals(hashHex, expected, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(tempFilePath);
                    throw new InvalidOperationException($"Checksum mismatch. Expected SHA-256: {expected}, Computed: {hashHex}");
                }
            }

            if (expectedSize.HasValue && expectedSize.Value != totalBytes)
            {
                TryDeleteFile(tempFilePath);
                throw new InvalidOperationException($"File size mismatch. Expected: {expectedSize.Value} bytes, Written: {totalBytes} bytes");
            }

            var shard1 = hashHex[..2];
            var shard2 = hashHex.Substring(2, 2);
            var relativePath = $"blobs/{shard1}/{shard2}/{blobId}.blob";
            var targetDirectory = Path.Combine(_storageRoot, "blobs", shard1, shard2);
            var finalFilePath = Path.Combine(targetDirectory, $"{blobId}.blob");

            EnsureDirectoryExists(targetDirectory);

            MoveWithRetry(tempFilePath, finalFilePath);

            _blobPathIndex[blobId] = finalFilePath;

            _logger.LogInformation("Saved direct blob {BlobId} at {RelativePath} ({SizeBytes} bytes)", blobId, relativePath, totalBytes);

            return new StorageBlobResult
            {
                BlobId = blobId,
                Sha256Hash = hashHex,
                SizeBytes = totalBytes,
                StorageRelativePath = relativePath
            };
        }
        catch (Exception ex)
        {
            TryDeleteFile(tempFilePath);
            if (ex is not InvalidOperationException)
            {
                _logger.LogError(ex, "Failed to write direct blob {BlobId}", blobId);
            }
            throw;
        }
    }

    public Task<(Stream Stream, long Length, string ContentType)?> OpenReadBlobAsync(
        Guid blobId,
        CancellationToken ct = default)
    {
        var filePath = LocateBlobFile(blobId);
        if (filePath == null || !File.Exists(filePath))
        {
            return Task.FromResult<(Stream Stream, long Length, string ContentType)?>(null);
        }

        var fileInfo = new FileInfo(filePath);
        Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, BufferSize, useAsync: true);

        return Task.FromResult<(Stream Stream, long Length, string ContentType)?>((stream, fileInfo.Length, "application/octet-stream"));
    }

    public Task<bool> DeleteBlobAsync(
        Guid blobId,
        CancellationToken ct = default)
    {
        var filePath = LocateBlobFile(blobId);
        if (filePath == null || !File.Exists(filePath))
        {
            return Task.FromResult(false);
        }

        try
        {
            File.Delete(filePath);
            _blobPathIndex.TryRemove(blobId, out _);

            TryCleanEmptyShards(filePath);
            _logger.LogInformation("Deleted blob {BlobId} from {Path}", blobId, filePath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while deleting blob {BlobId}", blobId);
            return Task.FromResult(false);
        }
    }

    public Task DeleteSessionTempDataAsync(Guid sessionId, CancellationToken ct = default)
    {
        var sessionDir = Path.Combine(_storageRoot, "temp", "sessions", sessionId.ToString("N"));
        TryDeleteDirectory(sessionDir);
        _logger.LogDebug("Cleaned temporary directory for session {SessionId}", sessionId);
        return Task.CompletedTask;
    }

    public Task<(int DeletedSessions, int DeletedFiles)> CleanupExpiredTempDataAsync(
        TimeSpan expiration,
        CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - expiration;
        int deletedSessions = 0;
        int deletedFiles = 0;

        // 1. Clean expired session directories
        var sessionsRoot = Path.Combine(_storageRoot, "temp", "sessions");
        if (Directory.Exists(sessionsRoot))
        {
            foreach (var dir in Directory.EnumerateDirectories(sessionsRoot))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.LastWriteTimeUtc < cutoff)
                    {
                        Directory.Delete(dir, recursive: true);
                        deletedSessions++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean expired session directory {Dir}", dir);
                }
            }
        }

        // 2. Clean orphaned tmp files in temp
        var tempRoot = Path.Combine(_storageRoot, "temp");
        if (Directory.Exists(tempRoot))
        {
            foreach (var file in Directory.EnumerateFiles(tempRoot, "*.tmp"))
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTimeUtc < cutoff)
                    {
                        File.Delete(file);
                        deletedFiles++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean orphaned tmp file {File}", file);
                }
            }
        }

        if (deletedSessions > 0 || deletedFiles > 0)
        {
            _logger.LogInformation("GC sweep: Cleaned {Sessions} expired sessions and {Files} orphaned tmp files",
                deletedSessions, deletedFiles);
        }

        return Task.FromResult((deletedSessions, deletedFiles));
    }

    public (long TotalBytes, long AvailableBytes, string DiskStatus) GetDiskTelemetry()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_storageRoot));
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    return (drive.TotalSize, drive.AvailableFreeSpace, "Healthy");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read drive info for storage root {StorageRoot}", _storageRoot);
        }

        return (0, 0, "Unknown");
    }

    private readonly object _cpuLock = new();
    private TimeSpan _lastCpuTime = TimeSpan.Zero;
    private DateTime _lastCpuSampleTime = DateTime.MinValue;

    public double? GetCpuUsage()
    {
        try
        {
            var proc = Process.GetCurrentProcess();
            var currentCpuTime = proc.TotalProcessorTime;
            var now = DateTime.UtcNow;

            lock (_cpuLock)
            {
                if (_lastCpuSampleTime == DateTime.MinValue)
                {
                    _lastCpuTime = currentCpuTime;
                    _lastCpuSampleTime = now;
                    return null;
                }

                var deltaCpu = (currentCpuTime - _lastCpuTime).TotalMilliseconds;
                var deltaWall = (now - _lastCpuSampleTime).TotalMilliseconds;

                _lastCpuTime = currentCpuTime;
                _lastCpuSampleTime = now;

                if (deltaWall > 0)
                {
                    var percent = (deltaCpu / (deltaWall * Environment.ProcessorCount)) * 100.0;
                    return Math.Round(Math.Clamp(percent, 0.0, 100.0), 1);
                }
            }
        }
        catch
        {
            // Ignore if process telemetry not supported on environment
        }

        return null;
    }

    private string? LocateBlobFile(Guid blobId)
    {
        if (_blobPathIndex.TryGetValue(blobId, out var cachedPath) && File.Exists(cachedPath))
        {
            return cachedPath;
        }

        var blobsDir = Path.Combine(_storageRoot, "blobs");
        if (!Directory.Exists(blobsDir))
        {
            return null;
        }

        var pattern = $"{blobId}.blob";
        var match = Directory.EnumerateFiles(blobsDir, pattern, SearchOption.AllDirectories).FirstOrDefault();
        if (match != null)
        {
            _blobPathIndex[blobId] = match;
            return match;
        }

        return null;
    }

    private void TryCleanEmptyShards(string filePath)
    {
        try
        {
            var parent = Path.GetDirectoryName(filePath);
            if (parent != null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
            {
                Directory.Delete(parent);
                var grandParent = Path.GetDirectoryName(parent);
                if (grandParent != null && Directory.Exists(grandParent) && !Directory.EnumerateFileSystemEntries(grandParent).Any())
                {
                    Directory.Delete(grandParent);
                }
            }
        }
        catch
        {
            // Ignore directory cleanup exceptions
        }
    }

    private static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch { }
    }

    private static void MoveWithRetry(string source, string destination, int maxRetries = 10)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && i < maxRetries - 1)
            {
                Thread.Sleep(25 * (i + 1));
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); } catch { }
                }
                Directory.Delete(path, recursive: true);
            }
        }
        catch { }
    }
}
