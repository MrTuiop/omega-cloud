using Microsoft.EntityFrameworkCore;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Helpers;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Application.DTOs.Upload;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Application.Services;

public class UploadService : IUploadService
{
    private const long MaxDirectStreamSizeBytes = 50 * 1024 * 1024; // 50 MB
    private const int DefaultChunkSizeBytes = 20 * 1024 * 1024;     // 20 MB

    private readonly IAppDbContext _dbContext;
    private readonly IFileService _fileService;
    private readonly IStorageDriverResolver _driverResolver;
    private readonly IRealtimeNotifier _notifier;
    private readonly IAuditService? _auditService;

    public UploadService(
        IAppDbContext dbContext,
        IFileService fileService,
        IStorageDriverResolver driverResolver,
        IRealtimeNotifier? notifier = null,
        IAuditService? auditService = null)
    {
        _dbContext = dbContext;
        _fileService = fileService;
        _driverResolver = driverResolver;
        _notifier = notifier ?? NullRealtimeNotifier.Instance;
        _auditService = auditService;
    }

    public async Task<FileItemDto> DirectStreamUploadAsync(UploadFileStreamRequest request, Guid userId, CancellationToken ct = default)
    {
        if (request.SizeBytes.HasValue && request.SizeBytes.Value > MaxDirectStreamSizeBytes)
        {
            throw new ValidationException($"File size exceeds the 50 MB limit for direct streaming ({MaxDirectStreamSizeBytes} bytes). Use chunked upload sessions for files larger than 50 MB.");
        }

        return await _fileService.UploadFileAsync(request, userId, ct);
    }

    public async Task<UploadSessionDto> InitSessionAsync(InitUploadSessionRequest request, Guid userId, CancellationToken ct = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedException("User not found.");

        if (!user.IsActive)
        {
            throw new UnauthorizedException("User account is inactive.");
        }

        var validatedFileName = FileHelper.ValidateFileName(request.FileName);

        var totalSizeBytes = request.TotalSizeBytes ?? request.Size ?? 0;
        if (totalSizeBytes <= 0)
        {
            throw new ValidationException("File size must be greater than zero.");
        }

        if (user.UsedQuotaBytes + totalSizeBytes > user.AllocatedQuotaBytes)
        {
            var remainingQuota = Math.Max(0, user.AllocatedQuotaBytes - user.UsedQuotaBytes);
            throw new ValidationException($"User quota exceeded. Available: {remainingQuota} bytes, Upload size: {totalSizeBytes} bytes.");
        }

        var targetFolderId = request.FolderId ?? request.TargetFolderId;
        if (targetFolderId.HasValue)
        {
            var folderExists = await _dbContext.Folders
                .AnyAsync(f => f.Id == targetFolderId.Value && f.UserId == userId && !f.IsDeleted, ct);

            if (!folderExists)
            {
                throw new NotFoundException($"Destination folder with ID '{targetFolderId.Value}' was not found.");
            }
        }

        var isDuplicate = await _dbContext.FileItems
            .AnyAsync(f => f.UserId == userId && f.FolderId == targetFolderId && f.Name == validatedFileName && !f.IsDeleted, ct);

        if (isDuplicate)
        {
            throw new ValidationException($"A file named '{validatedFileName}' already exists in this folder.");
        }

        var expectedSha256 = (request.ExpectedSha256 ?? request.Sha256)?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(expectedSha256))
        {
            if (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
            {
                throw new ValidationException("SHA-256 hash must be a 64-character hexadecimal string.");
            }
        }

        if (request.TotalChunks.HasValue && request.TotalChunks.Value <= 0)
        {
            throw new ValidationException("Total chunks must be greater than zero.");
        }

        if (request.ChunkSizeBytes.HasValue && request.ChunkSizeBytes.Value <= 0)
        {
            throw new ValidationException("Chunk size must be greater than zero.");
        }

        int chunkSizeBytes;
        int totalChunks;

        if (request.TotalChunks.HasValue && request.TotalChunks.Value > 0)
        {
            totalChunks = request.TotalChunks.Value;
            chunkSizeBytes = request.ChunkSizeBytes.HasValue && request.ChunkSizeBytes.Value > 0
                ? request.ChunkSizeBytes.Value
                : (int)Math.Ceiling((double)totalSizeBytes / totalChunks);
        }
        else if (request.ChunkSizeBytes.HasValue && request.ChunkSizeBytes.Value > 0)
        {
            chunkSizeBytes = request.ChunkSizeBytes.Value;
            totalChunks = (int)Math.Ceiling((double)totalSizeBytes / chunkSizeBytes);
        }
        else
        {
            chunkSizeBytes = DefaultChunkSizeBytes;
            totalChunks = (int)Math.Ceiling((double)totalSizeBytes / chunkSizeBytes);
        }

        if (totalChunks <= 0) totalChunks = 1;

        if (totalChunks > 50000)
        {
            throw new ValidationException("Total chunks cannot exceed 50,000.");
        }

        StorageNode? storageNode = null;

        if (request.StorageNodeId.HasValue)
        {
            storageNode = await _dbContext.StorageNodes
                .FirstOrDefaultAsync(n => n.Id == request.StorageNodeId.Value, ct);

            if (storageNode == null)
            {
                throw new NotFoundException($"Target storage node with ID '{request.StorageNodeId.Value}' was not found.");
            }

            if (!storageNode.IsActive || storageNode.Status != StorageNodeStatus.Online)
            {
                throw new ValidationException($"Target storage node '{storageNode.Name}' is currently {storageNode.Status}.");
            }
        }
        else
        {
            storageNode = await _dbContext.StorageNodes
                .FirstOrDefaultAsync(n => n.Type == StorageNodeType.LocalDisk && n.Status == StorageNodeStatus.Online, ct);

            if (storageNode == null)
            {
                storageNode = await _dbContext.StorageNodes
                    .FirstOrDefaultAsync(n => n.Status == StorageNodeStatus.Online, ct);
            }

            if (storageNode == null)
            {
                storageNode = await _dbContext.StorageNodes.FirstOrDefaultAsync(ct);
                if (storageNode == null)
                {
                    throw new InvalidOperationException("No storage node configured.");
                }
            }
        }

        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var expiresAt = now.AddHours(24);

        var session = new UploadSession
        {
            Id = sessionId,
            UserId = userId,
            TargetFolderId = targetFolderId,
            StorageNodeId = storageNode.Id,
            FileName = validatedFileName,
            TotalSizeBytes = totalSizeBytes,
            ChunkSizeBytes = chunkSizeBytes,
            TotalChunks = totalChunks,
            ExpectedSha256 = expectedSha256,
            Status = UploadSessionStatus.Created,
            TempStoragePath = $"temp/sessions/{sessionId:N}",
            CreatedAt = now,
            ExpiresAt = expiresAt
        };

        _dbContext.UploadSessions.Add(session);
        await _dbContext.SaveChangesAsync(ct);

        var driver = _driverResolver.GetDriverForNode(storageNode);
        var directUrl = await driver.TryGenerateDirectUploadUrlAsync(session, 0, TimeSpan.FromHours(1), ct);
        var uploadMode = directUrl != null ? "Direct" : "Proxy";

        return new UploadSessionDto
        {
            SessionId = session.Id,
            FileName = session.FileName,
            TotalSizeBytes = session.TotalSizeBytes,
            ChunkSizeBytes = session.ChunkSizeBytes,
            TotalChunks = session.TotalChunks,
            ExpectedSha256 = session.ExpectedSha256,
            UploadMode = uploadMode,
            TargetUrl = directUrl?.Url,
            Status = session.Status.ToString(),
            TargetFolderId = session.TargetFolderId,
            UploadedChunks = Array.Empty<int>(),
            MissingChunks = Enumerable.Range(0, session.TotalChunks).ToList(),
            ProgressPercentage = 0,
            CreatedAt = session.CreatedAt,
            ExpiresAt = session.ExpiresAt
        };
    }

    public async Task<UploadChunkResultDto> UploadChunkAsync(
        Guid sessionId,
        int chunkIndex,
        Stream chunkStream,
        long? contentLength,
        string? expectedChunkSha256,
        Guid userId,
        CancellationToken ct = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedException("User not found.");

        if (!user.IsActive)
        {
            throw new UnauthorizedException("User account is inactive.");
        }

        if (contentLength.HasValue && contentLength.Value <= 0)
        {
            throw new ValidationException("Chunk content cannot be empty.");
        }

        var session = await _dbContext.UploadSessions
            .Include(s => s.Chunks)
            .Include(s => s.StorageNode)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new NotFoundException($"Upload session with ID '{sessionId}' was not found.");

        if (session.Status == UploadSessionStatus.Completed)
        {
            throw new ValidationException("Upload session is already completed.");
        }
        if (session.Status == UploadSessionStatus.Aborted)
        {
            throw new ValidationException("Upload session has been aborted.");
        }
        if (session.Status == UploadSessionStatus.Expired || session.ExpiresAt < DateTime.UtcNow)
        {
            session.Status = UploadSessionStatus.Expired;
            await _dbContext.SaveChangesAsync(ct);
            throw new ValidationException("Upload session has expired.");
        }
        if (session.Status == UploadSessionStatus.Assembling)
        {
            throw new ValidationException("Upload session is currently assembling.");
        }

        if (chunkIndex < 0 || chunkIndex >= session.TotalChunks)
        {
            throw new ValidationException($"Invalid chunk index {chunkIndex}. Expected between 0 and {session.TotalChunks - 1}.");
        }

        var driver = _driverResolver.GetDriverForNode(session.StorageNode);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var writeResult = await driver.WriteChunkAsync(session, chunkIndex, chunkStream, expectedChunkSha256, ct);
        sw.Stop();

        if (writeResult.SizeBytes <= 0)
        {
            throw new ValidationException("Chunk content cannot be empty.");
        }

        if (writeResult.SizeBytes > session.TotalSizeBytes)
        {
            throw new ValidationException($"Chunk size ({writeResult.SizeBytes} bytes) exceeds total session size ({session.TotalSizeBytes} bytes).");
        }

        var chunk = session.Chunks.FirstOrDefault(c => c.ChunkIndex == chunkIndex);
        if (chunk == null)
        {
            chunk = new UploadChunk
            {
                Id = Guid.NewGuid(),
                UploadSessionId = session.Id,
                ChunkIndex = chunkIndex,
                SizeBytes = writeResult.SizeBytes,
                Sha256Hash = writeResult.Sha256Hash,
                IsUploaded = true,
                UploadedAt = DateTime.UtcNow
            };
            _dbContext.UploadChunks.Add(chunk);
        }
        else
        {
            chunk.SizeBytes = writeResult.SizeBytes;
            chunk.Sha256Hash = writeResult.Sha256Hash;
            chunk.IsUploaded = true;
            chunk.UploadedAt = DateTime.UtcNow;
        }

        session.Status = UploadSessionStatus.Uploading;
        await _dbContext.SaveChangesAsync(ct);

        var elapsedSec = sw.Elapsed.TotalSeconds;
        var speedBytesPerSec = elapsedSec > 0 ? Math.Round(writeResult.SizeBytes / elapsedSec, 2) : 0;

        var uploadedCount = session.Chunks.Count(c => c.IsUploaded);
        var percent = session.TotalChunks > 0 ? Math.Round((double)uploadedCount / session.TotalChunks * 100.0, 1) : 0;
        await _notifier.NotifyFileProgressAsync(session.UserId, session.Id, percent, speedBytesPerSec, ct);

        return new UploadChunkResultDto
        {
            SessionId = session.Id,
            ChunkIndex = chunkIndex,
            SizeBytes = writeResult.SizeBytes,
            Sha256Hash = writeResult.Sha256Hash,
            Status = "Saved"
        };
    }

    public async Task<FileItemDto> CompleteSessionAsync(
        Guid sessionId,
        CompleteUploadSessionRequest? request,
        Guid userId,
        CancellationToken ct = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedException("User not found.");

        if (!user.IsActive)
        {
            throw new UnauthorizedException("User account is inactive.");
        }

        var session = await _dbContext.UploadSessions
            .Include(s => s.Chunks)
            .Include(s => s.StorageNode)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new NotFoundException($"Upload session with ID '{sessionId}' was not found.");

        if (session.Status == UploadSessionStatus.Completed)
        {
            throw new ValidationException("Upload session is already completed.");
        }
        if (session.Status == UploadSessionStatus.Aborted)
        {
            throw new ValidationException("Upload session has been aborted.");
        }
        if (session.Status == UploadSessionStatus.Expired || session.ExpiresAt < DateTime.UtcNow)
        {
            session.Status = UploadSessionStatus.Expired;
            await _dbContext.SaveChangesAsync(ct);
            throw new ValidationException("Upload session has expired.");
        }
        if (session.Status == UploadSessionStatus.Assembling)
        {
            throw new ValidationException("Upload session is currently assembling.");
        }

        if (session.TargetFolderId.HasValue)
        {
            var folderExists = await _dbContext.Folders
                .AnyAsync(f => f.Id == session.TargetFolderId.Value && f.UserId == userId && !f.IsDeleted, ct);

            if (!folderExists)
            {
                throw new NotFoundException($"Destination folder with ID '{session.TargetFolderId.Value}' was not found or has been deleted.");
            }
        }

        var uploadedChunks = session.Chunks.Where(c => c.IsUploaded).ToList();
        var uploadedIndices = uploadedChunks.Select(c => c.ChunkIndex).ToHashSet();
        var missingIndices = Enumerable.Range(0, session.TotalChunks).Where(i => !uploadedIndices.Contains(i)).ToList();

        if (missingIndices.Count > 0)
        {
            throw new ValidationException($"Cannot complete upload session: missing {missingIndices.Count} chunk(s) [{string.Join(", ", missingIndices)}].");
        }

        var totalUploadedBytes = uploadedChunks.Sum(c => c.SizeBytes);
        if (totalUploadedBytes != session.TotalSizeBytes)
        {
            throw new ValidationException($"Total uploaded chunks size ({totalUploadedBytes} bytes) does not match expected session size ({session.TotalSizeBytes} bytes).");
        }

        if (user.UsedQuotaBytes + session.TotalSizeBytes > user.AllocatedQuotaBytes)
        {
            var remainingQuota = Math.Max(0, user.AllocatedQuotaBytes - user.UsedQuotaBytes);
            throw new ValidationException($"User quota exceeded. Available: {remainingQuota} bytes, Upload size: {session.TotalSizeBytes} bytes.");
        }

        var isDuplicate = await _dbContext.FileItems
            .AnyAsync(f => f.UserId == userId && f.FolderId == session.TargetFolderId && f.Name == session.FileName && !f.IsDeleted, ct);

        if (isDuplicate)
        {
            throw new ValidationException($"A file named '{session.FileName}' already exists in this folder.");
        }

        if (!string.IsNullOrWhiteSpace(request?.ExpectedSha256))
        {
            var cleanSha = request.ExpectedSha256.Trim().ToLowerInvariant();
            if (cleanSha.Length != 64 || !cleanSha.All(Uri.IsHexDigit))
            {
                throw new ValidationException("SHA-256 hash must be a 64-character hexadecimal string.");
            }

            if (!string.IsNullOrEmpty(session.ExpectedSha256) &&
                !string.Equals(session.ExpectedSha256, cleanSha, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException($"Provided SHA-256 does not match expected session SHA-256 ({session.ExpectedSha256}).");
            }

            session.ExpectedSha256 = cleanSha;
        }

        session.Status = UploadSessionStatus.Assembling;
        await _dbContext.SaveChangesAsync(ct);
        await _notifier.NotifyFileProgressAsync(session.UserId, session.Id, 100.0, 0, ct);

        var driver = _driverResolver.GetDriverForNode(session.StorageNode);
        StorageBlobResult assembleResult;
        try
        {
            assembleResult = await driver.AssembleChunksAsync(session, uploadedChunks, ct);
        }
        catch
        {
            session.Status = UploadSessionStatus.Uploading;
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            var uploadedCount = session.Chunks.Count(c => c.IsUploaded);
            var revertPercent = session.TotalChunks > 0 ? Math.Round((double)uploadedCount / session.TotalChunks * 100.0, 1) : 0;
            await _notifier.NotifyFileProgressAsync(session.UserId, session.Id, revertPercent, 0, CancellationToken.None);
            throw;
        }

        // Deduplication check: Has this exact blob already been stored on this node?
        var existingBlob = await _dbContext.StorageBlobs
            .FirstOrDefaultAsync(b => b.StorageNodeId == session.StorageNodeId &&
                                      b.Sha256Hash == assembleResult.Sha256Hash &&
                                      b.SizeBytes == assembleResult.SizeBytes, ct);

        StorageBlob targetBlob;
        if (existingBlob != null)
        {
            existingBlob.ReferenceCount++;
            targetBlob = existingBlob;

            try
            {
                await driver.DeleteAsync(new StorageBlob
                {
                    StorageRelativePath = assembleResult.StorageRelativePath
                }, ct);
            }
            catch
            {
                // Non-fatal if temp delete fails
            }
        }
        else
        {
            targetBlob = new StorageBlob
            {
                Id = assembleResult.BlobId,
                StorageNodeId = session.StorageNodeId,
                Sha256Hash = assembleResult.Sha256Hash,
                SizeBytes = assembleResult.SizeBytes,
                StorageRelativePath = assembleResult.StorageRelativePath,
                ReferenceCount = 1,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.StorageBlobs.Add(targetBlob);
        }

        var extension = Path.GetExtension(session.FileName).TrimStart('.').ToLowerInvariant();
        var mimeType = FileHelper.GetMimeType(session.FileName);
        var now = DateTime.UtcNow;

        var fileItem = new FileItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FolderId = session.TargetFolderId,
            StorageBlobId = targetBlob.Id,
            Name = session.FileName,
            Extension = extension,
            MimeType = mimeType,
            SizeBytes = targetBlob.SizeBytes,
            IsDeleted = false,
            IsFavorite = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.FileItems.Add(fileItem);
        user.UsedQuotaBytes += targetBlob.SizeBytes;
        session.Status = UploadSessionStatus.Completed;

        await _dbContext.SaveChangesAsync(ct);

        var resultDto = new FileItemDto
        {
            Id = fileItem.Id,
            UserId = fileItem.UserId,
            FolderId = fileItem.FolderId,
            StorageBlobId = fileItem.StorageBlobId,
            Name = fileItem.Name,
            Extension = fileItem.Extension,
            MimeType = fileItem.MimeType,
            SizeBytes = fileItem.SizeBytes,
            IsDeleted = fileItem.IsDeleted,
            DeletedAt = fileItem.DeletedAt,
            IsFavorite = fileItem.IsFavorite,
            CreatedAt = fileItem.CreatedAt,
            UpdatedAt = fileItem.UpdatedAt
        };

        await _notifier.NotifyFileProgressAsync(userId, session.Id, 100.0, 0, ct);
        await _notifier.NotifyDirectoryChangedAsync(userId, session.TargetFolderId, "Uploaded", resultDto, ct);
        await _notifier.NotifyQuotaUpdatedAsync(userId, user.UsedQuotaBytes, user.AllocatedQuotaBytes, ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                action: "FileUploaded",
                resource: "Files",
                resourceId: fileItem.Id.ToString(),
                userId: userId,
                username: user.Username,
                ipAddress: null,
                details: new { fileName = fileItem.Name, sizeBytes = fileItem.SizeBytes, sessionId = session.Id },
                ct: ct);
        }

        return resultDto;
    }

    public async Task<UploadSessionDto> GetSessionStatusAsync(Guid sessionId, Guid userId, int chunkIndex = 0, CancellationToken ct = default)
    {
        var session = await _dbContext.UploadSessions
            .Include(s => s.Chunks)
            .Include(s => s.StorageNode)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new NotFoundException($"Upload session with ID '{sessionId}' was not found.");

        var uploadedChunks = session.Chunks.Where(c => c.IsUploaded).Select(c => c.ChunkIndex).OrderBy(i => i).ToList();
        var uploadedSet = uploadedChunks.ToHashSet();
        var missingChunks = Enumerable.Range(0, session.TotalChunks).Where(i => !uploadedSet.Contains(i)).ToList();
        var uploadedBytes = session.Chunks.Where(c => c.IsUploaded).Sum(c => c.SizeBytes);
        var progress = session.TotalSizeBytes > 0 ? Math.Round((double)uploadedBytes / session.TotalSizeBytes * 100, 2) : 0;

        var driver = _driverResolver.GetDriverForNode(session.StorageNode);
        var targetChunkIndex = Math.Clamp(chunkIndex, 0, Math.Max(0, session.TotalChunks - 1));
        var directUrl = await driver.TryGenerateDirectUploadUrlAsync(session, targetChunkIndex, TimeSpan.FromHours(1), ct);

        return new UploadSessionDto
        {
            SessionId = session.Id,
            FileName = session.FileName,
            TotalSizeBytes = session.TotalSizeBytes,
            ChunkSizeBytes = session.ChunkSizeBytes,
            TotalChunks = session.TotalChunks,
            ExpectedSha256 = session.ExpectedSha256,
            UploadMode = directUrl != null ? "Direct" : "Proxy",
            TargetUrl = directUrl?.Url,
            Status = session.Status.ToString(),
            TargetFolderId = session.TargetFolderId,
            UploadedChunks = uploadedChunks,
            MissingChunks = missingChunks,
            ProgressPercentage = progress,
            CreatedAt = session.CreatedAt,
            ExpiresAt = session.ExpiresAt
        };
    }

    public async Task AbortSessionAsync(Guid sessionId, Guid userId, CancellationToken ct = default)
    {
        var session = await _dbContext.UploadSessions
            .Include(s => s.StorageNode)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new NotFoundException($"Upload session with ID '{sessionId}' was not found.");

        if (session.Status == UploadSessionStatus.Completed)
        {
            throw new ValidationException("Cannot abort an already completed session.");
        }

        session.Status = UploadSessionStatus.Aborted;
        var driver = _driverResolver.GetDriverForNode(session.StorageNode);
        await driver.DeleteSessionTempDataAsync(session, ct);

        await _dbContext.SaveChangesAsync(ct);
        await _notifier.NotifyFileProgressAsync(session.UserId, session.Id, 0, 0, ct);
    }
}
