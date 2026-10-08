using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Infrastructure.Storage;

public class LocalDiskStorageDriver : IStorageDriver
{
    private readonly string _storageRoot;
    private readonly ILogger<LocalDiskStorageDriver> _logger;

    public string DriverType => "LocalDisk";

    public LocalDiskStorageDriver(
        IOptions<LocalStorageOptions> options,
        ILogger<LocalDiskStorageDriver> logger)
    {
        _logger = logger;
        _storageRoot = Path.GetFullPath(options.Value.LocalRootPath ?? "./data/storage");

        EnsureDirectoryExists(_storageRoot);
    }

    // Constructor overload for direct root path (useful for testing)
    public LocalDiskStorageDriver(string storageRoot, ILogger<LocalDiskStorageDriver> logger)
    {
        _logger = logger;
        _storageRoot = Path.GetFullPath(storageRoot);

        EnsureDirectoryExists(_storageRoot);
    }

    public async Task<StorageBlobResult> WriteAsync(Stream contentStream, StorageWriteOptions options, CancellationToken ct = default)
    {
        var blobId = options.BlobId ?? Guid.NewGuid();
        var tempDir = Path.Combine(_storageRoot, "temp");
        EnsureDirectoryExists(tempDir);

        var tempFilePath = Path.Combine(tempDir, $"upload_{Guid.NewGuid():N}.tmp");

        long totalBytes = 0;
        string hashHex;

        try
        {
            using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
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

            if (!string.IsNullOrWhiteSpace(options.ExpectedSha256))
            {
                var expected = options.ExpectedSha256.Trim().ToLowerInvariant();
                if (!string.Equals(hashHex, expected, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(tempFilePath);
                    throw new ValidationException($"Checksum mismatch. Expected SHA-256: {expected}, Computed: {hashHex}");
                }
            }

            if (options.ExpectedSize.HasValue && options.ExpectedSize.Value != totalBytes)
            {
                TryDeleteFile(tempFilePath);
                throw new ValidationException($"File size mismatch. Expected: {options.ExpectedSize.Value} bytes, Written: {totalBytes} bytes");
            }

            // Sharding rule: {StorageRoot}/blobs/{hash[0..2]}/{hash[2..4]}/{blobId}.blob
            var shard1 = hashHex[..2];
            var shard2 = hashHex.Substring(2, 2);
            var relativePath = $"blobs/{shard1}/{shard2}/{blobId}.blob";
            var targetDirectory = Path.Combine(_storageRoot, "blobs", shard1, shard2);
            var finalFilePath = Path.Combine(targetDirectory, $"{blobId}.blob");

            EnsureDirectoryExists(targetDirectory);

            if (File.Exists(finalFilePath))
            {
                File.Delete(finalFilePath);
            }

            File.Move(tempFilePath, finalFilePath);

            _logger.LogDebug("Successfully saved blob {BlobId} at {RelativePath} ({SizeBytes} bytes)", blobId, relativePath, totalBytes);

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
            if (ex is ValidationException)
            {
                throw;
            }
            _logger.LogError(ex, "Failed to write stream to local disk storage for blob {BlobId}", blobId);
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(StorageBlob blob, CancellationToken ct = default)
    {
        var fullPath = ResolveAndValidatePath(blob.StorageRelativePath);

        if (!File.Exists(fullPath))
        {
            throw new NotFoundException($"Blob file '{blob.Id}' was not found at '{blob.StorageRelativePath}'.");
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(StorageBlob blob, CancellationToken ct = default)
    {
        try
        {
            var fullPath = ResolveAndValidatePath(blob.StorageRelativePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogDebug("Deleted blob file from disk: {Path}", fullPath);

                TryCleanEmptyShards(fullPath);
            }
        }
        catch (Exception ex) when (ex is not NotFoundException)
        {
            _logger.LogWarning(ex, "Error occurred while deleting blob file {StorageRelativePath}", blob.StorageRelativePath);
        }

        return Task.CompletedTask;
    }

    public Task<PreSignedUrlResult?> TryGenerateDirectDownloadUrlAsync(StorageBlob blob, TimeSpan validFor, CancellationToken ct = default)
    {
        // Local disk storage runs on the Master server and does not expose direct pre-signed URLs
        return Task.FromResult<PreSignedUrlResult?>(null);
    }

    public Task<PreSignedUrlResult?> TryGenerateDirectUploadUrlAsync(UploadSession session, int chunkIndex, TimeSpan validFor, CancellationToken ct = default)
    {
        return Task.FromResult<PreSignedUrlResult?>(null);
    }

    public Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            EnsureDirectoryExists(_storageRoot);
            var testFile = Path.Combine(_storageRoot, $".ping_{Guid.NewGuid():N}");
            File.WriteAllText(testFile, "ping");
            File.Delete(testFile);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ping failed for local storage root at {StorageRoot}", _storageRoot);
            return Task.FromResult(false);
        }
    }

    private string ResolveAndValidatePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ValidationException("Storage relative path cannot be empty.");
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar)
                                     .Replace('\\', Path.DirectorySeparatorChar)
                                     .TrimStart(Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(Path.Combine(_storageRoot, normalized));
        var rootWithSep = _storageRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _storageRoot
            : _storageRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) && !string.Equals(fullPath, _storageRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Access denied: path traversal detected.");
        }

        return fullPath;
    }

    private static void EnsureDirectoryExists(string dirPath)
    {
        if (!Directory.Exists(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Ignore failure on cleanup
        }
    }

    private void TryCleanEmptyShards(string filePath)
    {
        try
        {
            var shard2 = Path.GetDirectoryName(filePath);
            if (shard2 != null && Directory.Exists(shard2) && !Directory.EnumerateFileSystemEntries(shard2).Any())
            {
                Directory.Delete(shard2);
                var shard1 = Path.GetDirectoryName(shard2);
                if (shard1 != null && Directory.Exists(shard1) && !Directory.EnumerateFileSystemEntries(shard1).Any())
                {
                    Directory.Delete(shard1);
                }
            }
        }
        catch
        {
            // Shard cleanup is best-effort
        }
    }
}
