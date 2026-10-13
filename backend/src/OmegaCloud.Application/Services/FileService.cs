using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Helpers;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Application.Services;

public class FileService : IFileService
{
    private readonly IAppDbContext _dbContext;
    private readonly IStorageDriverResolver _driverResolver;
    private readonly IRealtimeNotifier _notifier;
    private readonly IAuditService? _auditService;
    private readonly IThumbnailService? _thumbnailService;

    public FileService(
        IAppDbContext dbContext,
        IStorageDriverResolver driverResolver,
        IRealtimeNotifier? notifier = null,
        IAuditService? auditService = null,
        IThumbnailService? thumbnailService = null)
    {
        _dbContext = dbContext;
        _driverResolver = driverResolver;
        _notifier = notifier ?? NullRealtimeNotifier.Instance;
        _auditService = auditService;
        _thumbnailService = thumbnailService;
    }

    public async Task<FileItemDto> UploadFileAsync(UploadFileStreamRequest request, Guid userId, CancellationToken ct = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedException("User not found.");

        if (!user.IsActive)
        {
            throw new UnauthorizedException("User account is inactive.");
        }

        if (request.SizeBytes.HasValue && request.SizeBytes.Value < 0)
        {
            throw new ValidationException("File size cannot be negative.");
        }

        if (request.SizeBytes.HasValue && request.SizeBytes.Value == 0)
        {
            throw new ValidationException("Cannot upload an empty file.");
        }

        if (request.SizeBytes.HasValue && request.SizeBytes.Value > 0)
        {
            if (user.UsedQuotaBytes + request.SizeBytes.Value > user.AllocatedQuotaBytes)
            {
                var remainingQuota = Math.Max(0, user.AllocatedQuotaBytes - user.UsedQuotaBytes);
                throw new ValidationException($"User quota exceeded. Available: {remainingQuota} bytes, File size: {request.SizeBytes.Value} bytes.");
            }
        }
        else
        {
            if (user.UsedQuotaBytes >= user.AllocatedQuotaBytes)
            {
                throw new ValidationException("User quota exceeded. No available storage space remaining.");
            }
        }

        var validatedFileName = ValidateFileName(request.FileName);

        Folder? targetFolder = null;
        if (request.FolderId.HasValue)
        {
            if (request.IsShared == true)
            {
                targetFolder = await _dbContext.Folders
                    .FirstOrDefaultAsync(f => f.Id == request.FolderId.Value && f.IsShared && !f.IsDeleted, ct);
            }
            else
            {
                targetFolder = await _dbContext.Folders
                    .FirstOrDefaultAsync(f => f.Id == request.FolderId.Value && (f.UserId == userId || f.IsShared) && !f.IsDeleted, ct);
            }

            if (targetFolder == null)
            {
                throw new NotFoundException($"Destination folder with ID '{request.FolderId.Value}' was not found.");
            }
        }

        var isShared = request.IsShared == true || (targetFolder?.IsShared ?? false);

        FileItem? existingFile = null;
        if (isShared)
        {
            existingFile = await _dbContext.FileItems
                .Include(f => f.StorageBlob)
                .FirstOrDefaultAsync(f => f.IsShared && f.FolderId == request.FolderId && f.Name == validatedFileName && !f.IsDeleted, ct);
        }
        else
        {
            existingFile = await _dbContext.FileItems
                .Include(f => f.StorageBlob)
                .FirstOrDefaultAsync(f => f.UserId == userId && !f.IsShared && f.FolderId == request.FolderId && f.Name == validatedFileName && !f.IsDeleted, ct);
        }

        if (existingFile != null)
        {
            var strategy = request.ConflictResolution ?? ConflictResolutionStrategy.None;
            if (strategy == ConflictResolutionStrategy.Skip)
            {
                return MapToFileDto(existingFile);
            }
            else if (strategy == ConflictResolutionStrategy.Rename)
            {
                var existingNames = await (isShared
                    ? _dbContext.FileItems.Where(f => f.IsShared && f.FolderId == request.FolderId && !f.IsDeleted).Select(f => f.Name).ToListAsync(ct)
                    : _dbContext.FileItems.Where(f => f.UserId == userId && f.FolderId == request.FolderId && !f.IsDeleted).Select(f => f.Name).ToListAsync(ct));
                var nameSet = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
                validatedFileName = FileHelper.GenerateUniqueFileName(validatedFileName, nameSet.Contains);
                existingFile = null;
            }
            else if (strategy == ConflictResolutionStrategy.Replace)
            {
                if (existingFile.IsShared && existingFile.UserId != userId && user.Role != UserRole.Admin)
                {
                    throw new ForbiddenException("You do not have permission to replace this shared file. Only the author or an admin can replace it.");
                }

                if (request.SizeBytes.HasValue && request.SizeBytes.Value > 0)
                {
                    if (existingFile.UserId == userId)
                    {
                        var quotaDiff = request.SizeBytes.Value - existingFile.SizeBytes;
                        if (quotaDiff > 0 && user.UsedQuotaBytes + quotaDiff > user.AllocatedQuotaBytes)
                        {
                            var remainingQuota = Math.Max(0, user.AllocatedQuotaBytes - user.UsedQuotaBytes);
                            throw new ValidationException($"User quota exceeded. Available: {remainingQuota} bytes, Required: {quotaDiff} bytes.");
                        }
                    }
                    else
                    {
                        if (user.UsedQuotaBytes + request.SizeBytes.Value > user.AllocatedQuotaBytes)
                        {
                            var remainingQuota = Math.Max(0, user.AllocatedQuotaBytes - user.UsedQuotaBytes);
                            throw new ValidationException($"User quota exceeded. Available: {remainingQuota} bytes, File size: {request.SizeBytes.Value} bytes.");
                        }
                    }
                }
            }
            else
            {
                throw new ValidationException($"A file named '{validatedFileName}' already exists in this folder.");
            }
        }

        StorageNode targetNode;
        if (request.StorageNodeId.HasValue)
        {
            targetNode = await _dbContext.StorageNodes
                .FirstOrDefaultAsync(n => n.Id == request.StorageNodeId.Value, ct)
                ?? throw new NotFoundException($"Target storage node with ID '{request.StorageNodeId.Value}' was not found.");

            if (!targetNode.IsActive || targetNode.Status != StorageNodeStatus.Online)
            {
                throw new ValidationException($"Target storage node '{targetNode.Name}' is not available (Status: {targetNode.Status}).");
            }
        }
        else
        {
            var candidateNodes = await _dbContext.StorageNodes
                .Where(n => n.IsActive && n.Status == StorageNodeStatus.Online)
                .ToListAsync(ct);

            if (candidateNodes.Count == 0)
            {
                candidateNodes = await _dbContext.StorageNodes
                    .Where(n => n.IsActive)
                    .ToListAsync(ct);
            }

            if (candidateNodes.Count == 0)
            {
                throw new InvalidOperationException("No online storage node is available.");
            }

            // Balanced node selection: active node with maximum free volume: MaxCapacityBytes - UsedBytes bounded by physical AvailableSpaceBytes
            targetNode = candidateNodes
                .OrderByDescending(n =>
                {
                    var usedBytes = Math.Max(0, n.TotalSpaceBytes - n.AvailableSpaceBytes);
                    var capacityRemaining = n.MaxCapacityBytes > 0 ? Math.Max(0, n.MaxCapacityBytes - usedBytes) : n.AvailableSpaceBytes;
                    return Math.Min(n.AvailableSpaceBytes, capacityRemaining);
                })
                .ThenBy(n => n.Type == StorageNodeType.LocalDisk ? 0 : 1)
                .First();
        }

        var driver = _driverResolver.GetDriverForNode(targetNode);

        var writeResult = await driver.WriteAsync(
            request.Content,
            new StorageWriteOptions
            {
                ExpectedSize = request.SizeBytes.HasValue && request.SizeBytes.Value > 0 ? request.SizeBytes.Value : null,
                ContentType = request.ContentType
            },
            ct);

        if (writeResult.SizeBytes <= 0)
        {
            throw new ValidationException("Cannot upload an empty file.");
        }

        if (user.UsedQuotaBytes + writeResult.SizeBytes > user.AllocatedQuotaBytes)
        {
            try
            {
                await driver.DeleteAsync(new StorageBlob
                {
                    StorageRelativePath = writeResult.StorageRelativePath
                }, ct);
            }
            catch
            {
                // Best-effort cleanup
            }

            var remainingQuota = Math.Max(0, user.AllocatedQuotaBytes - user.UsedQuotaBytes);
            throw new ValidationException($"User quota exceeded. Available: {remainingQuota} bytes, File size: {writeResult.SizeBytes} bytes.");
        }

        // Deduplication check: Has this exact blob already been stored on this node?
        var existingBlob = await _dbContext.StorageBlobs
            .FirstOrDefaultAsync(b => b.StorageNodeId == targetNode.Id &&
                                      b.Sha256Hash == writeResult.Sha256Hash &&
                                      b.SizeBytes == writeResult.SizeBytes, ct);

        StorageBlob targetBlob;

        if (existingBlob != null)
        {
            // Dedup match: increment reference count and discard duplicate physical file
            existingBlob.ReferenceCount++;
            targetBlob = existingBlob;

            try
            {
                await driver.DeleteAsync(new StorageBlob
                {
                    StorageRelativePath = writeResult.StorageRelativePath
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
                Id = writeResult.BlobId,
                StorageNodeId = targetNode.Id,
                Sha256Hash = writeResult.Sha256Hash,
                SizeBytes = writeResult.SizeBytes,
                StorageRelativePath = writeResult.StorageRelativePath,
                ReferenceCount = 1,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.StorageBlobs.Add(targetBlob);
        }

        var extension = Path.GetExtension(validatedFileName).TrimStart('.').ToLowerInvariant();
        var mimeType = string.IsNullOrWhiteSpace(request.ContentType)
            ? GetMimeType(validatedFileName)
            : request.ContentType;

        var now = DateTime.UtcNow;

        if (existingFile != null)
        {
            if (existingFile.UserId == userId)
            {
                var quotaDiff = writeResult.SizeBytes - existingFile.SizeBytes;
                if (quotaDiff > 0 && user.UsedQuotaBytes + quotaDiff > user.AllocatedQuotaBytes)
                {
                    try
                    {
                        await driver.DeleteAsync(new StorageBlob { StorageRelativePath = writeResult.StorageRelativePath }, ct);
                    }
                    catch {}
                    var remainingQuota = Math.Max(0, user.AllocatedQuotaBytes - user.UsedQuotaBytes);
                    throw new ValidationException($"User quota exceeded. Available: {remainingQuota} bytes, Required: {quotaDiff} bytes.");
                }
                user.UsedQuotaBytes += quotaDiff;
            }
            else
            {
                var oldAuthor = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == existingFile.UserId, ct);
                if (oldAuthor != null)
                {
                    oldAuthor.UsedQuotaBytes = Math.Max(0, oldAuthor.UsedQuotaBytes - existingFile.SizeBytes);
                    await _notifier.NotifyQuotaUpdatedAsync(oldAuthor.Id, oldAuthor.UsedQuotaBytes, oldAuthor.AllocatedQuotaBytes, ct);
                }
                user.UsedQuotaBytes += writeResult.SizeBytes;
                existingFile.UserId = userId;
            }

            var oldBlob = existingFile.StorageBlob;
            existingFile.StorageBlob = targetBlob;
            existingFile.StorageBlobId = targetBlob.Id;
            existingFile.SizeBytes = writeResult.SizeBytes;
            existingFile.MimeType = mimeType;
            existingFile.Extension = extension;
            existingFile.UpdatedAt = now;

            // Decrement old blob reference count if different blob
            if (oldBlob != null && oldBlob.Id != targetBlob.Id)
            {
                oldBlob.ReferenceCount = Math.Max(0, oldBlob.ReferenceCount - 1);
                if (oldBlob.ReferenceCount <= 0)
                {
                    try
                    {
                        var oldNode = await _dbContext.StorageNodes.FindAsync(new object[] { oldBlob.StorageNodeId }, ct);
                        if (oldNode != null)
                        {
                            var oldDriver = _driverResolver.GetDriverForNode(oldNode);
                            await oldDriver.DeleteAsync(oldBlob, ct);
                        }
                    }
                    catch {}
                    _dbContext.StorageBlobs.Remove(oldBlob);
                }
            }

            await _dbContext.SaveChangesAsync(ct);

            var replacedDto = MapToFileDto(existingFile);
            await _notifier.NotifyDirectoryChangedAsync(userId, request.FolderId, "Updated", replacedDto, ct);
            await _notifier.NotifyQuotaUpdatedAsync(userId, user.UsedQuotaBytes, user.AllocatedQuotaBytes, ct);

            if (_auditService != null)
            {
                await _auditService.LogAsync(
                    action: "FileReplaced",
                    resource: "Files",
                    resourceId: existingFile.Id.ToString(),
                    userId: userId,
                    username: user.Username,
                    ipAddress: null,
                    details: new { fileName = existingFile.Name, sizeBytes = existingFile.SizeBytes },
                    ct: ct);
            }

            return replacedDto;
        }

        var fileItem = new FileItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FolderId = request.FolderId,
            StorageBlobId = targetBlob.Id,
            Name = validatedFileName,
            Extension = extension,
            MimeType = mimeType,
            SizeBytes = writeResult.SizeBytes,
            IsShared = isShared,
            IsDeleted = false,
            IsFavorite = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.FileItems.Add(fileItem);
        user.UsedQuotaBytes += writeResult.SizeBytes;

        await _dbContext.SaveChangesAsync(ct);

        var dto = MapToFileDto(fileItem);
        await _notifier.NotifyDirectoryChangedAsync(userId, request.FolderId, "Created", dto, ct);
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
                details: new { fileName = fileItem.Name, sizeBytes = fileItem.SizeBytes },
                ct: ct);
        }

        return dto;
    }

    public async Task<FileMetadataDto> GetFileMetadataAsync(Guid fileId, Guid userId, CancellationToken ct = default)
    {
        var file = await _dbContext.FileItems
            .Include(f => f.Folder)
            .Include(f => f.StorageBlob)
                .ThenInclude(b => b.StorageNode)
            .FirstOrDefaultAsync(f => f.Id == fileId && f.UserId == userId && !f.IsDeleted, ct)
            ?? throw new NotFoundException($"File with ID '{fileId}' was not found.");

        return new FileMetadataDto
        {
            Id = file.Id,
            UserId = file.UserId,
            FolderId = file.FolderId,
            FolderName = file.Folder?.Name,
            StorageBlobId = file.StorageBlobId,
            Sha256Hash = file.StorageBlob.Sha256Hash,
            StorageNodeId = file.StorageBlob.StorageNodeId,
            StorageNodeName = file.StorageBlob.StorageNode.Name,
            StorageNodeType = file.StorageBlob.StorageNode.Type.ToString(),
            Name = file.Name,
            Extension = file.Extension,
            MimeType = file.MimeType,
            SizeBytes = file.SizeBytes,
            IsShared = file.IsShared,
            IsFavorite = file.IsFavorite,
            CreatedAt = file.CreatedAt,
            UpdatedAt = file.UpdatedAt
        };
    }

    public async Task<FileDownloadResult> GetFileDownloadStreamAsync(Guid fileId, Guid userId, CancellationToken ct = default)
    {
        var file = await _dbContext.FileItems
            .Include(f => f.StorageBlob)
                .ThenInclude(b => b.StorageNode)
            .FirstOrDefaultAsync(f => f.Id == fileId && f.UserId == userId && !f.IsDeleted, ct)
            ?? throw new NotFoundException($"File with ID '{fileId}' was not found.");

        var driver = _driverResolver.GetDriverForNode(file.StorageBlob.StorageNode);

        var directUrl = await driver.TryGenerateDirectDownloadUrlAsync(file.StorageBlob, TimeSpan.FromMinutes(15), ct);
        if (_auditService != null)
        {
            await _auditService.LogAsync(
                action: "FileDownloaded",
                resource: "Files",
                resourceId: file.Id.ToString(),
                userId: userId,
                username: null,
                ipAddress: null,
                details: new { fileName = file.Name, sizeBytes = file.SizeBytes, isDirect = directUrl != null },
                ct: ct);
        }

        if (directUrl != null)
        {
            return new FileDownloadResult
            {
                RedirectUrl = directUrl.Url,
                FileName = file.Name,
                ContentType = file.MimeType,
                SizeBytes = file.SizeBytes
            };
        }

        var stream = await driver.OpenReadAsync(file.StorageBlob, ct);
        return new FileDownloadResult
        {
            Stream = stream,
            FileName = file.Name,
            ContentType = file.MimeType,
            SizeBytes = file.SizeBytes
        };
    }

    public async Task<FileItemDto> UpdateFileAsync(Guid fileId, UpdateFileRequest request, Guid userId, CancellationToken ct = default)
    {
        var file = await _dbContext.FileItems
            .FirstOrDefaultAsync(f => f.Id == fileId && f.UserId == userId && !f.IsDeleted, ct)
            ?? throw new NotFoundException($"File with ID '{fileId}' was not found.");

        var oldFolderId = file.FolderId;
        var targetFolderId = file.FolderId;
        var isMoving = false;

        if (request.MoveToRoot == true)
        {
            targetFolderId = null;
            isMoving = true;
        }
        else if (request.FolderId.HasValue && request.FolderId.Value != file.FolderId)
        {
            targetFolderId = request.FolderId.Value;
            isMoving = true;
        }

        var targetName = file.Name;
        var isRenaming = false;
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var newName = ValidateFileName(request.Name);
            if (!string.Equals(newName, file.Name, StringComparison.Ordinal))
            {
                targetName = newName;
                isRenaming = true;
            }
        }

        if (isMoving && targetFolderId.HasValue)
        {
            var folderExists = await _dbContext.Folders
                .AnyAsync(f => f.Id == targetFolderId.Value && f.UserId == userId && !f.IsDeleted, ct);

            if (!folderExists)
            {
                throw new NotFoundException($"Destination folder with ID '{targetFolderId.Value}' was not found.");
            }
        }

        if (isRenaming || isMoving)
        {
            var isDuplicate = await _dbContext.FileItems
                .AnyAsync(f => f.UserId == userId && f.FolderId == targetFolderId && f.Name == targetName && f.Id != file.Id && !f.IsDeleted, ct);

            if (isDuplicate)
            {
                throw new ValidationException($"A file named '{targetName}' already exists in the destination folder.");
            }
        }

        if (isRenaming)
        {
            file.Name = targetName;
            file.Extension = Path.GetExtension(targetName).TrimStart('.').ToLowerInvariant();
        }

        if (isMoving)
        {
            file.FolderId = targetFolderId;
        }

        if (request.IsFavorite.HasValue)
        {
            file.IsFavorite = request.IsFavorite.Value;
        }

        file.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        var dto = MapToFileDto(file);
        if (isMoving && oldFolderId != file.FolderId)
        {
            // Notify both source and destination folders so both views refresh reactively
            await _notifier.NotifyDirectoryChangedAsync(userId, oldFolderId, "Moved", dto, ct);
            await _notifier.NotifyDirectoryChangedAsync(userId, file.FolderId, "Moved", dto, ct);
            await _notifier.NotifyFileProgressAsync(userId, file.Id, 100.0, 0, ct);
        }
        else
        {
            await _notifier.NotifyDirectoryChangedAsync(userId, file.FolderId, "Updated", dto, ct);
        }

        return dto;
    }

    public async Task DeleteFileAsync(Guid fileId, Guid userId, CancellationToken ct = default)
    {
        var file = await _dbContext.FileItems
            .FirstOrDefaultAsync(f => f.Id == fileId && f.UserId == userId && !f.IsDeleted, ct)
            ?? throw new NotFoundException($"File with ID '{fileId}' was not found.");

        file.IsDeleted = true;
        file.DeletedAt = DateTime.UtcNow;
        file.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);

        var dto = MapToFileDto(file);
        await _notifier.NotifyDirectoryChangedAsync(userId, file.FolderId, "Deleted", dto, ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                action: "FileDeleted",
                resource: "Files",
                resourceId: file.Id.ToString(),
                userId: userId,
                username: null,
                ipAddress: null,
                details: new { fileName = file.Name, sizeBytes = file.SizeBytes },
                ct: ct);
        }
    }

    public async Task<IReadOnlyList<string>> CheckConflictsAsync(CheckFileConflictsRequest request, Guid userId, CancellationToken ct = default)
    {
        var details = await CheckConflictDetailsAsync(request, userId, ct);
        return details.Select(d => d.FileName).ToList();
    }

    public async Task<IReadOnlyList<FileConflictDetailDto>> CheckConflictDetailsAsync(CheckFileConflictsRequest request, Guid userId, CancellationToken ct = default)
    {
        if (request.FileNames == null || request.FileNames.Count == 0)
        {
            return Array.Empty<FileConflictDetailDto>();
        }

        var isShared = request.IsShared == true;
        if (!isShared && request.FolderId.HasValue)
        {
            var folder = await _dbContext.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == request.FolderId.Value && !f.IsDeleted, ct);
            if (folder != null && folder.IsShared)
            {
                isShared = true;
            }
        }

        var requestedNames = request.FileNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var query = _dbContext.FileItems
            .AsNoTracking()
            .Where(f => !f.IsDeleted && f.FolderId == request.FolderId && requestedNames.Contains(f.Name));

        if (isShared)
        {
            query = query.Where(f => f.IsShared);
        }
        else
        {
            query = query.Where(f => f.UserId == userId);
        }

        var existingFiles = await query.ToListAsync(ct);

        return existingFiles.Select(f => new FileConflictDetailDto
        {
            FileName = f.Name,
            ExistingFileId = f.Id,
            SizeBytes = f.SizeBytes,
            UpdatedAt = f.UpdatedAt,
            IsShared = f.IsShared
        }).ToList();
    }

    public async Task<ThumbnailResult> GetFileThumbnailAsync(Guid fileId, Guid? userId, int width = 256, int height = 256, string format = "webp", CancellationToken ct = default)
    {
        var file = await _dbContext.FileItems
            .Include(f => f.StorageBlob)
                .ThenInclude(b => b.StorageNode)
            .FirstOrDefaultAsync(f => f.Id == fileId && !f.IsDeleted, ct)
            ?? throw new NotFoundException($"File with ID '{fileId}' was not found.");

        if (!file.IsShared)
        {
            var isAllowed = false;
            if (userId.HasValue)
            {
                if (file.UserId == userId.Value)
                {
                    isAllowed = true;
                }
                else
                {
                    var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
                    if (user?.Role == UserRole.Admin)
                    {
                        isAllowed = true;
                    }
                }
            }

            if (!isAllowed)
            {
                throw new NotFoundException($"File with ID '{fileId}' was not found.");
            }
        }

        if (_thumbnailService != null)
        {
            var cached = _thumbnailService.TryGetCachedThumbnail(
                file.StorageBlob.Sha256Hash,
                width,
                height,
                format,
                file.MimeType);

            if (cached != null)
            {
                return cached;
            }
        }

        var driver = _driverResolver.GetDriverForNode(file.StorageBlob.StorageNode);
        await using var readStream = await driver.OpenReadAsync(file.StorageBlob, ct);

        if (_thumbnailService != null)
        {
            return await _thumbnailService.GetOrCreateThumbnailAsync(
                readStream,
                file.Name,
                file.MimeType,
                file.StorageBlob.Sha256Hash,
                width,
                height,
                format,
                ct);
        }

        return new ThumbnailResult
        {
            Stream = readStream,
            ContentType = file.MimeType,
            ETag = $"\"{file.StorageBlob.Sha256Hash}\""
        };
    }

    public async Task<FileDownloadResult> GetFileStreamAsync(Guid fileId, Guid? userId, CancellationToken ct = default)
    {
        var file = await _dbContext.FileItems
            .Include(f => f.StorageBlob)
                .ThenInclude(b => b.StorageNode)
            .FirstOrDefaultAsync(f => f.Id == fileId && !f.IsDeleted, ct)
            ?? throw new NotFoundException($"File with ID '{fileId}' was not found.");

        if (!file.IsShared)
        {
            var isAllowed = false;
            if (userId.HasValue)
            {
                if (file.UserId == userId.Value)
                {
                    isAllowed = true;
                }
                else
                {
                    var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
                    if (user?.Role == UserRole.Admin)
                    {
                        isAllowed = true;
                    }
                }
            }

            if (!isAllowed)
            {
                throw new NotFoundException($"File with ID '{fileId}' was not found.");
            }
        }

        var driver = _driverResolver.GetDriverForNode(file.StorageBlob.StorageNode);
        var directUrl = await driver.TryGenerateDirectDownloadUrlAsync(file.StorageBlob, TimeSpan.FromMinutes(15), ct);
        if (directUrl != null)
        {
            return new FileDownloadResult
            {
                RedirectUrl = directUrl.Url,
                FileName = file.Name,
                ContentType = file.MimeType,
                SizeBytes = file.SizeBytes
            };
        }

        var stream = await driver.OpenReadAsync(file.StorageBlob, ct);

        return new FileDownloadResult
        {
            Stream = stream,
            FileName = file.Name,
            ContentType = file.MimeType,
            SizeBytes = file.SizeBytes
        };
    }

    public async Task<FileDownloadResult> DownloadFilesZipAsync(IEnumerable<Guid> fileIds, IEnumerable<Guid>? folderIds, Guid userId, CancellationToken ct = default)
    {
        var targetFileIds = new HashSet<Guid>(fileIds ?? Enumerable.Empty<Guid>());
        var folderIdList = folderIds?.ToList() ?? new List<Guid>();
        var rootFolderSet = new HashSet<Guid>(folderIdList);
        var expandedFolderIds = new HashSet<Guid>();
        var folderMap = new Dictionary<Guid, Folder>();

        if (folderIdList.Count > 0)
        {
            var allUserFolders = await _dbContext.Folders
                .AsNoTracking()
                .Where(f => f.UserId == userId && !f.IsDeleted)
                .ToListAsync(ct);

            folderMap = allUserFolders.ToDictionary(f => f.Id);

            foreach (var fid in folderIdList.Where(id => folderMap.ContainsKey(id)))
            {
                expandedFolderIds.Add(fid);
                var descendants = GetDescendantFolderIds(fid, allUserFolders);
                foreach (var d in descendants)
                {
                    expandedFolderIds.Add(d);
                }
            }

            var filesInFolders = await _dbContext.FileItems
                .AsNoTracking()
                .Where(f => f.UserId == userId && !f.IsDeleted && f.FolderId != null && expandedFolderIds.Contains(f.FolderId.Value))
                .Select(f => f.Id)
                .ToListAsync(ct);

            foreach (var fid in filesInFolders)
            {
                targetFileIds.Add(fid);
            }
        }

        var files = await _dbContext.FileItems
            .Include(f => f.StorageBlob)
                .ThenInclude(b => b.StorageNode)
            .Where(f => targetFileIds.Contains(f.Id) && f.UserId == userId && !f.IsDeleted)
            .ToListAsync(ct);

        if (files.Count == 0 && expandedFolderIds.Count == 0)
        {
            throw new NotFoundException("No files or folders found to download.");
        }

        string GetFolderPath(Guid fId)
        {
            var currentId = (Guid?)fId;
            var parts = new List<string>();
            while (currentId.HasValue && folderMap.TryGetValue(currentId.Value, out var f))
            {
                parts.Add(f.Name);
                if (rootFolderSet.Contains(f.Id) || !f.ParentFolderId.HasValue)
                {
                    break;
                }
                currentId = f.ParentFolderId;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Create directory entries for empty folders
            foreach (var fid in expandedFolderIds)
            {
                var folderPath = GetFolderPath(fid) + "/";
                if (!usedPaths.Contains(folderPath))
                {
                    archive.CreateEntry(folderPath);
                    usedPaths.Add(folderPath);
                }
            }

            foreach (var file in files)
            {
                string relativeEntryPath;
                if (file.FolderId.HasValue && expandedFolderIds.Contains(file.FolderId.Value))
                {
                    var prefix = GetFolderPath(file.FolderId.Value);
                    relativeEntryPath = $"{prefix}/{file.Name}";
                }
                else
                {
                    relativeEntryPath = file.Name;
                }

                if (usedPaths.Contains(relativeEntryPath))
                {
                    var lastSlash = relativeEntryPath.LastIndexOf('/');
                    var dir = lastSlash >= 0 ? relativeEntryPath[..lastSlash] : string.Empty;
                    var fileName = lastSlash >= 0 ? relativeEntryPath[(lastSlash + 1)..] : relativeEntryPath;
                    var ext = Path.GetExtension(fileName);
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    var counter = 1;

                    while (true)
                    {
                        var candidateName = $"{nameWithoutExt} ({counter}){ext}";
                        var candidatePath = string.IsNullOrEmpty(dir) ? candidateName : $"{dir}/{candidateName}";
                        if (!usedPaths.Contains(candidatePath))
                        {
                            relativeEntryPath = candidatePath;
                            break;
                        }
                        counter++;
                    }
                }
                usedPaths.Add(relativeEntryPath);

                var entry = archive.CreateEntry(relativeEntryPath, CompressionLevel.Optimal);
                var driver = _driverResolver.GetDriverForNode(file.StorageBlob.StorageNode);
                using var fileStream = await driver.OpenReadAsync(file.StorageBlob, ct);
                using var entryStream = entry.Open();
                await fileStream.CopyToAsync(entryStream, ct);
            }
        }

        memoryStream.Position = 0;

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                action: "BulkFilesDownloadedZip",
                resource: "Files",
                resourceId: string.Join(",", files.Select(f => f.Id)),
                userId: userId,
                username: null,
                ipAddress: null,
                details: new { fileCount = files.Count, totalBytes = memoryStream.Length },
                ct: ct);
        }

        return new FileDownloadResult
        {
            Stream = memoryStream,
            FileName = "omega-cloud-archive.zip",
            ContentType = "application/zip",
            SizeBytes = memoryStream.Length
        };
    }

    public async Task BulkDeleteAsync(IEnumerable<Guid>? fileIds, IEnumerable<Guid>? folderIds, Guid userId, CancellationToken ct = default)
    {
        var touchedFolderIds = new HashSet<Guid?>();

        if (fileIds != null)
        {
            var files = await _dbContext.FileItems
                .Where(f => fileIds.Contains(f.Id) && f.UserId == userId && !f.IsDeleted)
                .ToListAsync(ct);

            foreach (var file in files)
            {
                file.IsDeleted = true;
                file.DeletedAt = DateTime.UtcNow;
                file.UpdatedAt = DateTime.UtcNow;
                touchedFolderIds.Add(file.FolderId);
            }
        }

        if (folderIds != null)
        {
            var folderIdList = folderIds.ToList();
            var allFolders = await _dbContext.Folders
                .Where(f => f.UserId == userId && !f.IsDeleted)
                .ToListAsync(ct);

            var foldersToDelete = new HashSet<Guid>(folderIdList);
            foreach (var fid in folderIdList)
            {
                var descendants = GetDescendantFolderIds(fid, allFolders);
                foreach (var d in descendants)
                {
                    foldersToDelete.Add(d);
                }
            }

            foreach (var f in allFolders.Where(f => foldersToDelete.Contains(f.Id)))
            {
                f.IsDeleted = true;
                f.DeletedAt = DateTime.UtcNow;
                f.UpdatedAt = DateTime.UtcNow;
                touchedFolderIds.Add(f.ParentFolderId);
            }

            var nestedFiles = await _dbContext.FileItems
                .Where(f => f.UserId == userId && !f.IsDeleted && f.FolderId != null && foldersToDelete.Contains(f.FolderId.Value))
                .ToListAsync(ct);

            foreach (var nf in nestedFiles)
            {
                nf.IsDeleted = true;
                nf.DeletedAt = DateTime.UtcNow;
                nf.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        foreach (var parentId in touchedFolderIds)
        {
            await _notifier.NotifyDirectoryChangedAsync(userId, parentId, "BulkDeleted", null, ct);
        }
    }

    public async Task BulkMoveAsync(IEnumerable<Guid>? fileIds, IEnumerable<Guid>? folderIds, Guid? targetFolderId, Guid userId, CancellationToken ct = default)
    {
        if (targetFolderId.HasValue)
        {
            var destFolderExists = await _dbContext.Folders
                .AnyAsync(f => f.Id == targetFolderId.Value && f.UserId == userId && !f.IsDeleted, ct);
            if (!destFolderExists)
            {
                throw new NotFoundException($"Destination folder with ID '{targetFolderId.Value}' was not found.");
            }
        }

        var touchedFolderIds = new HashSet<Guid?> { targetFolderId };

        if (fileIds != null)
        {
            var files = await _dbContext.FileItems
                .Where(f => fileIds.Contains(f.Id) && f.UserId == userId && !f.IsDeleted)
                .ToListAsync(ct);

            foreach (var file in files)
            {
                if (file.FolderId != targetFolderId)
                {
                    var isDuplicate = await _dbContext.FileItems
                        .AnyAsync(f => f.UserId == userId && f.FolderId == targetFolderId && f.Name == file.Name && f.Id != file.Id && !f.IsDeleted, ct);
                    if (!isDuplicate)
                    {
                        touchedFolderIds.Add(file.FolderId);
                        file.FolderId = targetFolderId;
                        file.UpdatedAt = DateTime.UtcNow;
                    }
                }
            }
        }

        if (folderIds != null)
        {
            var allFolders = await _dbContext.Folders
                .Where(f => f.UserId == userId && !f.IsDeleted)
                .ToListAsync(ct);

            var foldersToMove = allFolders.Where(f => folderIds.Contains(f.Id)).ToList();

            foreach (var folder in foldersToMove)
            {
                if (folder.ParentFolderId == targetFolderId) continue;

                if (targetFolderId.HasValue)
                {
                    if (folder.Id == targetFolderId.Value)
                    {
                        throw new ValidationException("A folder cannot be moved into itself.");
                    }
                    var descendants = GetDescendantFolderIds(folder.Id, allFolders);
                    if (descendants.Contains(targetFolderId.Value))
                    {
                        throw new ValidationException("A folder cannot be moved into one of its own subfolders.");
                    }
                }

                var isDuplicate = await _dbContext.Folders
                    .AnyAsync(f => f.UserId == userId && f.ParentFolderId == targetFolderId && f.Name == folder.Name && f.Id != folder.Id && !f.IsDeleted, ct);
                if (!isDuplicate)
                {
                    touchedFolderIds.Add(folder.ParentFolderId);
                    folder.ParentFolderId = targetFolderId;
                    folder.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        foreach (var fid in touchedFolderIds)
        {
            await _notifier.NotifyDirectoryChangedAsync(userId, fid, "BulkMoved", null, ct);
        }
    }

    public async Task CopyToSharedAsync(IEnumerable<Guid>? fileIds, IEnumerable<Guid>? folderIds, Guid? targetSharedFolderId, Guid userId, CancellationToken ct = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException($"User with ID '{userId}' was not found.");

        if (!user.IsActive)
        {
            throw new UnauthorizedException("User account is inactive.");
        }

        if (targetSharedFolderId.HasValue)
        {
            var destFolder = await _dbContext.Folders
                .FirstOrDefaultAsync(f => f.Id == targetSharedFolderId.Value && f.IsShared && !f.IsDeleted, ct);
            if (destFolder == null)
            {
                throw new NotFoundException($"Shared destination folder with ID '{targetSharedFolderId.Value}' was not found.");
            }
        }

        var fileIdList = fileIds?.ToList() ?? new List<Guid>();
        var folderIdList = folderIds?.ToList() ?? new List<Guid>();

        if (fileIdList.Count == 0 && folderIdList.Count == 0)
        {
            return;
        }

        // 1. Direct personal files
        var files = fileIdList.Count > 0
            ? await _dbContext.FileItems
                .Include(f => f.StorageBlob)
                .Where(f => fileIdList.Contains(f.Id) && f.UserId == userId && !f.IsDeleted && !f.IsShared)
                .ToListAsync(ct)
            : new List<FileItem>();

        // 2. Personal folders
        var allUserFolders = await _dbContext.Folders
            .Where(f => f.UserId == userId && !f.IsShared && !f.IsDeleted)
            .ToListAsync(ct);

        var topFolders = folderIdList.Count > 0
            ? allUserFolders.Where(f => folderIdList.Contains(f.Id)).ToList()
            : new List<Folder>();

        if (topFolders.Count > 1)
        {
            var selectedIds = new HashSet<Guid>(topFolders.Select(f => f.Id));
            topFolders = topFolders.Where(f =>
            {
                var curr = f.ParentFolderId;
                while (curr.HasValue)
                {
                    if (selectedIds.Contains(curr.Value)) return false;
                    var parent = allUserFolders.FirstOrDefault(p => p.Id == curr.Value);
                    curr = parent?.ParentFolderId;
                }
                return true;
            }).ToList();
        }

        var allCopiedFolderIds = new HashSet<Guid>();
        foreach (var tf in topFolders)
        {
            allCopiedFolderIds.Add(tf.Id);
            foreach (var descId in GetDescendantFolderIds(tf.Id, allUserFolders))
            {
                allCopiedFolderIds.Add(descId);
            }
        }

        // Exclude any personal file that is already inside one of the folders being copied
        if (allCopiedFolderIds.Count > 0)
        {
            files = files.Where(f => !f.FolderId.HasValue || !allCopiedFolderIds.Contains(f.FolderId.Value)).ToList();
        }

        // Names in target location for conflict resolution
        var existingFileNames = await _dbContext.FileItems
            .Where(f => f.IsShared && f.FolderId == targetSharedFolderId && !f.IsDeleted)
            .Select(f => f.Name)
            .ToListAsync(ct);
        var fileNameSet = new HashSet<string>(existingFileNames, StringComparer.OrdinalIgnoreCase);

        var existingFolderNames = await _dbContext.Folders
            .Where(f => f.IsShared && f.ParentFolderId == targetSharedFolderId && !f.IsDeleted)
            .Select(f => f.Name)
            .ToListAsync(ct);
        var folderNameSet = new HashSet<string>(existingFolderNames, StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        int copiedFilesCount = 0;
        int copiedFoldersCount = 0;

        foreach (var file in files)
        {
            var uniqueName = FileHelper.GenerateUniqueFileName(file.Name, n => fileNameSet.Contains(n));
            fileNameSet.Add(uniqueName);

            var newFile = new FileItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FolderId = targetSharedFolderId,
                StorageBlobId = file.StorageBlobId,
                Name = uniqueName,
                Extension = file.Extension,
                MimeType = file.MimeType,
                SizeBytes = file.SizeBytes,
                IsShared = true,
                IsDeleted = false,
                IsFavorite = false,
                CreatedAt = now,
                UpdatedAt = now
            };

            _dbContext.FileItems.Add(newFile);

            var blob = file.StorageBlob ?? await _dbContext.StorageBlobs.FindAsync(new object[] { file.StorageBlobId }, ct);
            if (blob != null)
            {
                blob.ReferenceCount++;
            }

            copiedFilesCount++;
        }

        if (topFolders.Count > 0)
        {
            var folderMapping = new Dictionary<Guid, Guid>();
            var userFolderLookup = allUserFolders
                .Where(f => f.ParentFolderId.HasValue)
                .GroupBy(f => f.ParentFolderId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var tf in topFolders)
            {
                var uniqueFolderName = FileHelper.GenerateUniqueFolderName(tf.Name, n => folderNameSet.Contains(n));
                folderNameSet.Add(uniqueFolderName);

                var newTopFolder = new Folder
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    ParentFolderId = targetSharedFolderId,
                    Name = uniqueFolderName,
                    Color = tf.Color,
                    IsShared = true,
                    IsDeleted = false,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _dbContext.Folders.Add(newTopFolder);
                folderMapping[tf.Id] = newTopFolder.Id;
                copiedFoldersCount++;

                var queue = new Queue<Guid>();
                queue.Enqueue(tf.Id);

                while (queue.Count > 0)
                {
                    var parentSourceId = queue.Dequeue();
                    var mappedParentId = folderMapping[parentSourceId];

                    if (userFolderLookup.TryGetValue(parentSourceId, out var childFolders))
                    {
                        foreach (var cf in childFolders)
                        {
                            var newChildFolder = new Folder
                            {
                                Id = Guid.NewGuid(),
                                UserId = userId,
                                ParentFolderId = mappedParentId,
                                Name = cf.Name,
                                Color = cf.Color,
                                IsShared = true,
                                IsDeleted = false,
                                CreatedAt = now,
                                UpdatedAt = now
                            };

                            _dbContext.Folders.Add(newChildFolder);
                            folderMapping[cf.Id] = newChildFolder.Id;
                            copiedFoldersCount++;
                            queue.Enqueue(cf.Id);
                        }
                    }
                }
            }

            var allCopiedSourceFolderIds = folderMapping.Keys.ToList();
            var nestedFiles = await _dbContext.FileItems
                .Include(f => f.StorageBlob)
                .Where(f => f.UserId == userId && !f.IsShared && !f.IsDeleted && f.FolderId != null && allCopiedSourceFolderIds.Contains(f.FolderId.Value))
                .ToListAsync(ct);

            foreach (var nf in nestedFiles)
            {
                var mappedParentId = folderMapping[nf.FolderId!.Value];
                var newFile = new FileItem
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    FolderId = mappedParentId,
                    StorageBlobId = nf.StorageBlobId,
                    Name = nf.Name,
                    Extension = nf.Extension,
                    MimeType = nf.MimeType,
                    SizeBytes = nf.SizeBytes,
                    IsShared = true,
                    IsDeleted = false,
                    IsFavorite = false,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _dbContext.FileItems.Add(newFile);

                var blob = nf.StorageBlob ?? await _dbContext.StorageBlobs.FindAsync(new object[] { nf.StorageBlobId }, ct);
                if (blob != null)
                {
                    blob.ReferenceCount++;
                }

                copiedFilesCount++;
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        await _notifier.NotifyDirectoryChangedAsync(userId, targetSharedFolderId, "DirectoryChanged", null, ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                action: "FilesCopiedToShared",
                resource: "Shared",
                resourceId: targetSharedFolderId?.ToString(),
                userId: userId,
                username: user.Username,
                ipAddress: null,
                details: new { fileCount = copiedFilesCount, folderCount = copiedFoldersCount, targetSharedFolderId },
                ct: ct);
        }
    }

    public async Task SaveToPersonalAsync(IEnumerable<Guid>? fileIds, IEnumerable<Guid>? folderIds, Guid? targetFolderId, Guid userId, CancellationToken ct = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException($"User with ID '{userId}' was not found.");

        if (!user.IsActive)
        {
            throw new UnauthorizedException("User account is inactive.");
        }

        if (targetFolderId.HasValue)
        {
            var destFolder = await _dbContext.Folders
                .FirstOrDefaultAsync(f => f.Id == targetFolderId.Value && f.UserId == userId && !f.IsShared && !f.IsDeleted, ct);
            if (destFolder == null)
            {
                throw new NotFoundException($"Destination folder with ID '{targetFolderId.Value}' was not found.");
            }
        }

        var fileIdList = fileIds?.ToList() ?? new List<Guid>();
        var folderIdList = folderIds?.ToList() ?? new List<Guid>();

        if (fileIdList.Count == 0 && folderIdList.Count == 0)
        {
            return;
        }

        // 1. Direct shared files
        var files = fileIdList.Count > 0
            ? await _dbContext.FileItems
                .Include(f => f.StorageBlob)
                .Where(f => fileIdList.Contains(f.Id) && f.IsShared && !f.IsDeleted)
                .ToListAsync(ct)
            : new List<FileItem>();

        // 2. Shared folders
        var allSharedFolders = await _dbContext.Folders
            .Where(f => f.IsShared && !f.IsDeleted)
            .ToListAsync(ct);

        var topFolders = folderIdList.Count > 0
            ? allSharedFolders.Where(f => folderIdList.Contains(f.Id)).ToList()
            : new List<Folder>();

        if (topFolders.Count > 1)
        {
            var selectedIds = new HashSet<Guid>(topFolders.Select(f => f.Id));
            topFolders = topFolders.Where(f =>
            {
                var curr = f.ParentFolderId;
                while (curr.HasValue)
                {
                    if (selectedIds.Contains(curr.Value)) return false;
                    var parent = allSharedFolders.FirstOrDefault(p => p.Id == curr.Value);
                    curr = parent?.ParentFolderId;
                }
                return true;
            }).ToList();
        }

        var allCopiedSharedFolderIds = new HashSet<Guid>();
        foreach (var tf in topFolders)
        {
            allCopiedSharedFolderIds.Add(tf.Id);
            foreach (var descId in GetDescendantFolderIds(tf.Id, allSharedFolders))
            {
                allCopiedSharedFolderIds.Add(descId);
            }
        }

        // Exclude any file that is already inside one of the shared folders being copied
        if (allCopiedSharedFolderIds.Count > 0)
        {
            files = files.Where(f => !f.FolderId.HasValue || !allCopiedSharedFolderIds.Contains(f.FolderId.Value)).ToList();
        }

        var nestedFiles = allCopiedSharedFolderIds.Count > 0
            ? await _dbContext.FileItems
                .Include(f => f.StorageBlob)
                .Where(f => f.IsShared && !f.IsDeleted && f.FolderId != null && allCopiedSharedFolderIds.Contains(f.FolderId.Value))
                .ToListAsync(ct)
            : new List<FileItem>();

        // Check user quota
        long totalRequiredBytes = files.Sum(f => f.SizeBytes) + nestedFiles.Sum(f => f.SizeBytes);

        if (user.UsedQuotaBytes + totalRequiredBytes > user.AllocatedQuotaBytes)
        {
            var remainingQuota = Math.Max(0, user.AllocatedQuotaBytes - user.UsedQuotaBytes);
            throw new ValidationException(
                $"User quota exceeded. Available: {remainingQuota} bytes, Required: {totalRequiredBytes} bytes.");
        }

        // Names in target location for conflict resolution
        var existingFileNames = await _dbContext.FileItems
            .Where(f => !f.IsShared && f.UserId == userId && f.FolderId == targetFolderId && !f.IsDeleted)
            .Select(f => f.Name)
            .ToListAsync(ct);
        var fileNameSet = new HashSet<string>(existingFileNames, StringComparer.OrdinalIgnoreCase);

        var existingFolderNames = await _dbContext.Folders
            .Where(f => !f.IsShared && f.UserId == userId && f.ParentFolderId == targetFolderId && !f.IsDeleted)
            .Select(f => f.Name)
            .ToListAsync(ct);
        var folderNameSet = new HashSet<string>(existingFolderNames, StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        int copiedFilesCount = 0;
        int copiedFoldersCount = 0;

        foreach (var file in files)
        {
            var uniqueName = FileHelper.GenerateUniqueFileName(file.Name, n => fileNameSet.Contains(n));
            fileNameSet.Add(uniqueName);

            var newFile = new FileItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FolderId = targetFolderId,
                StorageBlobId = file.StorageBlobId,
                Name = uniqueName,
                Extension = file.Extension,
                MimeType = file.MimeType,
                SizeBytes = file.SizeBytes,
                IsShared = false,
                IsDeleted = false,
                IsFavorite = false,
                CreatedAt = now,
                UpdatedAt = now
            };

            _dbContext.FileItems.Add(newFile);

            var blob = file.StorageBlob ?? await _dbContext.StorageBlobs.FindAsync(new object[] { file.StorageBlobId }, ct);
            if (blob != null)
            {
                blob.ReferenceCount++;
            }

            copiedFilesCount++;
        }

        if (topFolders.Count > 0)
        {
            var folderMapping = new Dictionary<Guid, Guid>();
            var sharedFolderLookup = allSharedFolders
                .Where(f => f.ParentFolderId.HasValue)
                .GroupBy(f => f.ParentFolderId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var tf in topFolders)
            {
                var uniqueFolderName = FileHelper.GenerateUniqueFolderName(tf.Name, n => folderNameSet.Contains(n));
                folderNameSet.Add(uniqueFolderName);

                var newTopFolder = new Folder
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    ParentFolderId = targetFolderId,
                    Name = uniqueFolderName,
                    Color = tf.Color,
                    IsShared = false,
                    IsDeleted = false,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _dbContext.Folders.Add(newTopFolder);
                folderMapping[tf.Id] = newTopFolder.Id;
                copiedFoldersCount++;

                var queue = new Queue<Guid>();
                queue.Enqueue(tf.Id);

                while (queue.Count > 0)
                {
                    var parentSourceId = queue.Dequeue();
                    var mappedParentId = folderMapping[parentSourceId];

                    if (sharedFolderLookup.TryGetValue(parentSourceId, out var childFolders))
                    {
                        foreach (var cf in childFolders)
                        {
                            var newChildFolder = new Folder
                            {
                                Id = Guid.NewGuid(),
                                UserId = userId,
                                ParentFolderId = mappedParentId,
                                Name = cf.Name,
                                Color = cf.Color,
                                IsShared = false,
                                IsDeleted = false,
                                CreatedAt = now,
                                UpdatedAt = now
                            };

                            _dbContext.Folders.Add(newChildFolder);
                            folderMapping[cf.Id] = newChildFolder.Id;
                            copiedFoldersCount++;
                            queue.Enqueue(cf.Id);
                        }
                    }
                }
            }

            foreach (var nf in nestedFiles)
            {
                var mappedParentId = folderMapping[nf.FolderId!.Value];
                var newFile = new FileItem
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    FolderId = mappedParentId,
                    StorageBlobId = nf.StorageBlobId,
                    Name = nf.Name,
                    Extension = nf.Extension,
                    MimeType = nf.MimeType,
                    SizeBytes = nf.SizeBytes,
                    IsShared = false,
                    IsDeleted = false,
                    IsFavorite = false,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _dbContext.FileItems.Add(newFile);

                var blob = nf.StorageBlob ?? await _dbContext.StorageBlobs.FindAsync(new object[] { nf.StorageBlobId }, ct);
                if (blob != null)
                {
                    blob.ReferenceCount++;
                }

                copiedFilesCount++;
            }
        }

        user.UsedQuotaBytes += totalRequiredBytes;
        user.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(ct);

        await _notifier.NotifyDirectoryChangedAsync(userId, targetFolderId, "DirectoryChanged", null, ct);
        await _notifier.NotifyQuotaUpdatedAsync(userId, user.UsedQuotaBytes, user.AllocatedQuotaBytes, ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                action: "FilesSavedToPersonal",
                resource: "Files",
                resourceId: targetFolderId?.ToString(),
                userId: userId,
                username: user.Username,
                ipAddress: null,
                details: new { fileCount = copiedFilesCount, folderCount = copiedFoldersCount, targetFolderId, totalBytes = totalRequiredBytes },
                ct: ct);
        }
    }


    public async Task<IReadOnlyList<FileItemDto>> GetRecentFilesAsync(Guid userId, int limit = 10, CancellationToken ct = default)
    {
        var files = await _dbContext.FileItems
            .AsNoTracking()
            .Where(f => f.UserId == userId && !f.IsDeleted)
            .OrderByDescending(f => f.UpdatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return files.Select(MapToFileDto).ToList();
    }

    public async Task<OmegaCloud.Application.DTOs.Folders.FolderContentDto> SearchAsync(
        string query,
        Guid? folderId,
        string scope,
        Guid userId,
        Guid[]? folderIds = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new OmegaCloud.Application.DTOs.Folders.FolderContentDto();
        }

        query = query.Trim().ToLowerInvariant();

        IQueryable<Folder> foldersBaseQuery = _dbContext.Folders
            .AsNoTracking()
            .Where(f => !f.IsDeleted);

        if (scope.Equals("shared", StringComparison.OrdinalIgnoreCase))
        {
            foldersBaseQuery = foldersBaseQuery.Where(f => f.IsShared);
        }
        else if (scope.Equals("personal", StringComparison.OrdinalIgnoreCase))
        {
            foldersBaseQuery = foldersBaseQuery.Where(f => f.UserId == userId && !f.IsShared);
        }
        else
        {
            foldersBaseQuery = foldersBaseQuery.Where(f => f.UserId == userId || f.IsShared);
        }

        var allCandidateFolders = await foldersBaseQuery.ToListAsync(ct);

        List<Guid>? allowedFolderIds = null;
        if (folderIds != null && folderIds.Length > 0)
        {
            allowedFolderIds = new List<Guid>();
            foreach (var fId in folderIds)
            {
                allowedFolderIds.Add(fId);
                var descendants = GetDescendantFolderIds(fId, allCandidateFolders);
                allowedFolderIds.AddRange(descendants);
            }
            allowedFolderIds = allowedFolderIds.Distinct().ToList();
        }
        else if (scope.Equals("current", StringComparison.OrdinalIgnoreCase) && folderId.HasValue)
        {
            allowedFolderIds = GetDescendantFolderIds(folderId.Value, allCandidateFolders);
            allowedFolderIds.Add(folderId.Value);
        }

        var matchingFolders = allCandidateFolders
            .Where(f => f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) && (allowedFolderIds == null || allowedFolderIds.Contains(f.Id)))
            .Select(f => new OmegaCloud.Application.DTOs.Folders.FolderDto
            {
                Id = f.Id,
                UserId = f.UserId,
                ParentFolderId = f.ParentFolderId,
                Name = f.Name,
                Color = f.Color,
                IsShared = f.IsShared,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt
            })
            .ToList();

        IQueryable<FileItem> filesQuery = _dbContext.FileItems
            .AsNoTracking()
            .Where(f => !f.IsDeleted && f.Name.ToLower().Contains(query));

        if (scope.Equals("shared", StringComparison.OrdinalIgnoreCase))
        {
            filesQuery = filesQuery.Where(f => f.IsShared);
        }
        else if (scope.Equals("personal", StringComparison.OrdinalIgnoreCase))
        {
            filesQuery = filesQuery.Where(f => f.UserId == userId && !f.IsShared);
        }
        else
        {
            filesQuery = filesQuery.Where(f => f.UserId == userId || f.IsShared);
        }

        if (allowedFolderIds != null)
        {
            filesQuery = filesQuery.Where(f => f.FolderId.HasValue && allowedFolderIds.Contains(f.FolderId.Value));
        }

        var matchingFiles = await filesQuery
            .OrderByDescending(f => f.UpdatedAt)
            .Take(100)
            .ToListAsync(ct);

        return new OmegaCloud.Application.DTOs.Folders.FolderContentDto
        {
            Folders = matchingFolders,
            Files = matchingFiles.Select(MapToFileDto).ToList()
        };
    }

    private static List<Guid> GetDescendantFolderIds(Guid folderId, List<Folder> allFolders)
    {
        var descendants = new List<Guid>();
        var visited = new HashSet<Guid> { folderId };
        var queue = new Queue<Guid>();
        queue.Enqueue(folderId);

        var lookup = allFolders
            .Where(f => f.ParentFolderId.HasValue)
            .GroupBy(f => f.ParentFolderId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (lookup.TryGetValue(current, out var children))
            {
                foreach (var child in children)
                {
                    if (visited.Add(child.Id))
                    {
                        descendants.Add(child.Id);
                        queue.Enqueue(child.Id);
                    }
                }
            }
        }

        return descendants;
    }

    private static string ValidateFileName(string? name) => FileHelper.ValidateFileName(name);

    private static string GetMimeType(string fileName) => FileHelper.GetMimeType(fileName);

    private static FileItemDto MapToFileDto(FileItem f) => new FileItemDto
    {
        Id = f.Id,
        UserId = f.UserId,
        FolderId = f.FolderId,
        StorageBlobId = f.StorageBlobId,
        Name = f.Name,
        Extension = f.Extension,
        MimeType = f.MimeType,
        SizeBytes = f.SizeBytes,
        IsShared = f.IsShared,
        IsDeleted = f.IsDeleted,
        DeletedAt = f.DeletedAt,
        IsFavorite = f.IsFavorite,
        CreatedAt = f.CreatedAt,
        UpdatedAt = f.UpdatedAt
    };
}
