using Microsoft.EntityFrameworkCore;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Folders;
using OmegaCloud.Application.DTOs.Trash;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Application.Services;

public class TrashService : ITrashService
{
    private readonly IAppDbContext _dbContext;
    private readonly IStorageDriverResolver _driverResolver;
    private readonly IRealtimeNotifier _notifier;
    private readonly IAuditService? _auditService;

    public TrashService(
        IAppDbContext dbContext,
        IStorageDriverResolver driverResolver,
        IRealtimeNotifier? notifier = null,
        IAuditService? auditService = null)
    {
        _dbContext = dbContext;
        _driverResolver = driverResolver;
        _notifier = notifier ?? NullRealtimeNotifier.Instance;
        _auditService = auditService;
    }

    public async Task<TrashContentDto> GetTrashContentAsync(Guid userId, bool topLevelOnly = false, CancellationToken ct = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedException("User not found.");

        if (!user.IsActive)
        {
            throw new UnauthorizedException("User account is inactive.");
        }

        var deletedFolders = await _dbContext.Folders
            .AsNoTracking()
            .Where(f => f.UserId == userId && f.IsDeleted)
            .OrderByDescending(f => f.DeletedAt)
            .ToListAsync(ct);

        var deletedFiles = await _dbContext.FileItems
            .AsNoTracking()
            .Include(f => f.Folder)
            .Where(f => f.UserId == userId && f.IsDeleted)
            .OrderByDescending(f => f.DeletedAt)
            .ToListAsync(ct);

        List<Folder> resultFolders;
        List<FileItem> resultFiles;

        if (topLevelOnly)
        {
            var deletedFolderIds = deletedFolders.Select(f => f.Id).ToHashSet();

            resultFolders = deletedFolders
                .Where(f => f.ParentFolderId == null || !deletedFolderIds.Contains(f.ParentFolderId.Value))
                .ToList();

            resultFiles = deletedFiles
                .Where(f => f.FolderId == null || !deletedFolderIds.Contains(f.FolderId.Value))
                .ToList();
        }
        else
        {
            resultFolders = deletedFolders;
            resultFiles = deletedFiles;
        }

        var totalTrashSizeBytes = deletedFiles.Sum(f => f.SizeBytes);
        var totalItemCount = deletedFolders.Count + deletedFiles.Count;

        var allocated = user.AllocatedQuotaBytes;
        var used = user.UsedQuotaBytes;
        var available = Math.Max(0, allocated - used);

        var quotaDto = new UserQuotaDto
        {
            AllocatedBytes = allocated,
            UsedBytes = used,
            AvailableBytes = available
        };

        return new TrashContentDto
        {
            Folders = resultFolders.Select(f => new TrashFolderDto
            {
                Id = f.Id,
                UserId = f.UserId,
                ParentFolderId = f.ParentFolderId,
                Name = f.Name,
                Color = f.Color,
                DeletedAt = f.DeletedAt,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt
            }).ToList(),
            Files = resultFiles.Select(f => new TrashFileDto
            {
                Id = f.Id,
                UserId = f.UserId,
                FolderId = f.FolderId,
                FolderName = f.Folder?.Name,
                StorageBlobId = f.StorageBlobId,
                Name = f.Name,
                Extension = f.Extension,
                MimeType = f.MimeType,
                SizeBytes = f.SizeBytes,
                IsFavorite = f.IsFavorite,
                DeletedAt = f.DeletedAt,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt
            }).ToList(),
            TotalTrashSizeBytes = totalTrashSizeBytes,
            TotalItemCount = totalItemCount,
            Quota = quotaDto
        };
    }

    public async Task<RestoreItemResultDto> RestoreItemAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedException("User not found.");

        if (!user.IsActive)
        {
            throw new UnauthorizedException("User account is inactive.");
        }

        // 1. Try to find a deleted file with this ID
        var file = await _dbContext.FileItems
            .FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId && f.IsDeleted, ct);

        if (file != null)
        {
            Guid? targetFolderId = file.FolderId;
            var restoredToRoot = false;

            if (targetFolderId.HasValue)
            {
                var destinationFolderActive = await _dbContext.Folders
                    .AnyAsync(f => f.Id == targetFolderId.Value && f.UserId == userId && !f.IsDeleted, ct);

                if (!destinationFolderActive)
                {
                    targetFolderId = null;
                    restoredToRoot = true;
                }
            }

            var duplicateExists = await _dbContext.FileItems
                .AnyAsync(f => f.UserId == userId && f.FolderId == targetFolderId && f.Name == file.Name && !f.IsDeleted && f.Id != file.Id, ct);

            if (duplicateExists)
            {
                throw new ValidationException($"A file named '{file.Name}' already exists in the destination location. Please rename or delete the existing file before restoring.");
            }

            file.FolderId = targetFolderId;
            file.IsDeleted = false;
            file.DeletedAt = null;
            file.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(ct);

            var fileResult = new RestoreItemResultDto
            {
                Id = file.Id,
                ItemType = "File",
                Name = file.Name,
                RestoredToFolderId = targetFolderId,
                RestoredToRoot = restoredToRoot,
                RestoredSubfoldersCount = 0,
                RestoredFilesCount = 1,
                Message = $"File '{file.Name}' restored successfully."
            };

            await _notifier.NotifyDirectoryChangedAsync(userId, targetFolderId, "Restored", fileResult, ct);

            return fileResult;
        }

        // 2. Try to find a deleted folder with this ID
        var folder = await _dbContext.Folders
            .FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId && f.IsDeleted, ct);

        if (folder != null)
        {
            Guid? targetParentId = folder.ParentFolderId;
            var restoredToRoot = false;

            if (targetParentId.HasValue)
            {
                var parentFolderActive = await _dbContext.Folders
                    .AnyAsync(f => f.Id == targetParentId.Value && f.UserId == userId && !f.IsDeleted, ct);

                if (!parentFolderActive)
                {
                    targetParentId = null;
                    restoredToRoot = true;
                }
            }

            var duplicateExists = await _dbContext.Folders
                .AnyAsync(f => f.UserId == userId && f.ParentFolderId == targetParentId && f.Name == folder.Name && !f.IsDeleted && f.Id != folder.Id, ct);

            if (duplicateExists)
            {
                throw new ValidationException($"A folder named '{folder.Name}' already exists in the destination location. Please rename or delete the existing folder before restoring.");
            }

            var now = DateTime.UtcNow;
            folder.ParentFolderId = targetParentId;
            folder.IsDeleted = false;
            folder.DeletedAt = null;
            folder.UpdatedAt = now;

            var allUserDeletedFolders = await _dbContext.Folders
                .Where(f => f.UserId == userId && f.IsDeleted && f.Id != folder.Id)
                .ToListAsync(ct);

            var descendantFolderIds = GetDescendantFolderIds(folder.Id, allUserDeletedFolders);

            // Group by parent and name to avoid duplicate active subfolders within the same parent
            var subfoldersToRestore = allUserDeletedFolders
                .Where(f => descendantFolderIds.Contains(f.Id))
                .GroupBy(f => new { f.ParentFolderId, f.Name })
                .Select(g => g.OrderByDescending(f => f.DeletedAt ?? f.UpdatedAt).First())
                .ToList();

            var restoredSubfoldersCount = 0;
            foreach (var sub in subfoldersToRestore)
            {
                sub.IsDeleted = false;
                sub.DeletedAt = null;
                sub.UpdatedAt = now;
                restoredSubfoldersCount++;
            }

            var affectedFolderIds = subfoldersToRestore.Select(s => s.Id).Concat(new[] { folder.Id }).ToList();

            var allSubtreeDeletedFiles = await _dbContext.FileItems
                .Where(f => f.UserId == userId && f.FolderId != null && affectedFolderIds.Contains(f.FolderId.Value) && f.IsDeleted)
                .ToListAsync(ct);

            // Group by folder and name to avoid duplicate active files within the same parent
            var filesToRestore = allSubtreeDeletedFiles
                .GroupBy(f => new { f.FolderId, f.Name })
                .Select(g => g.OrderByDescending(f => f.DeletedAt ?? f.UpdatedAt).First())
                .ToList();

            foreach (var f in filesToRestore)
            {
                f.IsDeleted = false;
                f.DeletedAt = null;
                f.UpdatedAt = now;
            }

            await _dbContext.SaveChangesAsync(ct);

            var folderResult = new RestoreItemResultDto
            {
                Id = folder.Id,
                ItemType = "Folder",
                Name = folder.Name,
                RestoredToFolderId = targetParentId,
                RestoredToRoot = restoredToRoot,
                RestoredSubfoldersCount = restoredSubfoldersCount,
                RestoredFilesCount = filesToRestore.Count,
                Message = $"Folder '{folder.Name}' restored successfully."
            };

            await _notifier.NotifyDirectoryChangedAsync(userId, targetParentId, "Restored", folderResult, ct);

            return folderResult;
        }

        throw new NotFoundException($"Item with ID '{id}' was not found in trash.");
    }

    public async Task PermanentlyDeleteItemAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedException("User not found.");

        if (!user.IsActive)
        {
            throw new UnauthorizedException("User account is inactive.");
        }

        // 1. Try file
        var file = await _dbContext.FileItems
            .Include(f => f.StorageBlob)
                .ThenInclude(b => b.StorageNode)
            .FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId && f.IsDeleted, ct);

        if (file != null)
        {
            user.UsedQuotaBytes = Math.Max(0, user.UsedQuotaBytes - file.SizeBytes);

            var blob = file.StorageBlob;
            _dbContext.FileItems.Remove(file);

            if (blob != null)
            {
                blob.ReferenceCount = Math.Max(0, blob.ReferenceCount - 1);
                if (blob.ReferenceCount <= 0)
                {
                    if (blob.StorageNode != null)
                    {
                        var driver = _driverResolver.GetDriverForNode(blob.StorageNode);
                        await driver.DeleteAsync(blob, ct);
                    }
                    _dbContext.StorageBlobs.Remove(blob);
                }
            }

            await _dbContext.SaveChangesAsync(ct);
            var filePayload = new { Id = file.Id, Name = file.Name, Type = "File" };
            await _notifier.NotifyDirectoryChangedAsync(userId, file.FolderId, "PermanentlyDeleted", filePayload, ct);
            await _notifier.NotifyQuotaUpdatedAsync(userId, user.UsedQuotaBytes, user.AllocatedQuotaBytes, ct);
            return;
        }

        // 2. Try folder
        var folder = await _dbContext.Folders
            .FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId && f.IsDeleted, ct);

        if (folder != null)
        {
            var allUserDeletedFolders = await _dbContext.Folders
                .Where(f => f.UserId == userId && f.IsDeleted && f.Id != folder.Id)
                .ToListAsync(ct);

            var descendantFolderIds = GetDescendantFolderIds(folder.Id, allUserDeletedFolders);
            var allAffectedFolderIds = descendantFolderIds.Concat(new[] { folder.Id }).ToList();

            var subtreeFiles = await _dbContext.FileItems
                .Include(f => f.StorageBlob)
                    .ThenInclude(b => b.StorageNode)
                .Where(f => f.UserId == userId && f.FolderId != null && allAffectedFolderIds.Contains(f.FolderId.Value) && f.IsDeleted)
                .ToListAsync(ct);

            var removedBlobIds = new HashSet<Guid>();

            foreach (var f in subtreeFiles)
            {
                user.UsedQuotaBytes = Math.Max(0, user.UsedQuotaBytes - f.SizeBytes);

                var blob = f.StorageBlob;
                _dbContext.FileItems.Remove(f);

                if (blob != null)
                {
                    blob.ReferenceCount = Math.Max(0, blob.ReferenceCount - 1);
                    if (blob.ReferenceCount <= 0 && removedBlobIds.Add(blob.Id))
                    {
                        if (blob.StorageNode != null)
                        {
                            var driver = _driverResolver.GetDriverForNode(blob.StorageNode);
                            await driver.DeleteAsync(blob, ct);
                        }
                        _dbContext.StorageBlobs.Remove(blob);
                    }
                }
            }

            var foldersToDelete = allUserDeletedFolders
                .Where(f => descendantFolderIds.Contains(f.Id))
                .Append(folder)
                .ToList();

            // Null out parent foreign keys first and save to avoid self-referencing FK restrict violations
            foreach (var f in foldersToDelete)
            {
                f.ParentFolderId = null;
            }
            await _dbContext.SaveChangesAsync(ct);

            _dbContext.Folders.RemoveRange(foldersToDelete);
            await _dbContext.SaveChangesAsync(ct);
            var folderPayload = new { Id = folder.Id, Name = folder.Name, Type = "Folder" };
            await _notifier.NotifyDirectoryChangedAsync(userId, folder.ParentFolderId, "PermanentlyDeleted", folderPayload, ct);
            await _notifier.NotifyQuotaUpdatedAsync(userId, user.UsedQuotaBytes, user.AllocatedQuotaBytes, ct);
            return;
        }

        throw new NotFoundException($"Item with ID '{id}' was not found in trash.");
    }

    public async Task<EmptyTrashResultDto> EmptyTrashAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedException("User not found.");

        if (!user.IsActive)
        {
            throw new UnauthorizedException("User account is inactive.");
        }

        var deletedFiles = await _dbContext.FileItems
            .Include(f => f.StorageBlob)
                .ThenInclude(b => b.StorageNode)
            .Where(f => f.UserId == userId && f.IsDeleted)
            .ToListAsync(ct);

        var deletedFolders = await _dbContext.Folders
            .Where(f => f.UserId == userId && f.IsDeleted)
            .ToListAsync(ct);

        long freedBytes = 0;
        var removedBlobIds = new HashSet<Guid>();

        foreach (var file in deletedFiles)
        {
            freedBytes += file.SizeBytes;
            user.UsedQuotaBytes = Math.Max(0, user.UsedQuotaBytes - file.SizeBytes);

            var blob = file.StorageBlob;
            _dbContext.FileItems.Remove(file);

            if (blob != null)
            {
                blob.ReferenceCount = Math.Max(0, blob.ReferenceCount - 1);
                if (blob.ReferenceCount <= 0 && removedBlobIds.Add(blob.Id))
                {
                    if (blob.StorageNode != null)
                    {
                        var driver = _driverResolver.GetDriverForNode(blob.StorageNode);
                        await driver.DeleteAsync(blob, ct);
                    }
                    _dbContext.StorageBlobs.Remove(blob);
                }
            }
        }

        foreach (var folder in deletedFolders)
        {
            folder.ParentFolderId = null;
        }
        await _dbContext.SaveChangesAsync(ct);

        _dbContext.Folders.RemoveRange(deletedFolders);
        user.UsedQuotaBytes = Math.Max(0, user.UsedQuotaBytes);

        await _dbContext.SaveChangesAsync(ct);

        await _notifier.NotifyDirectoryChangedAsync(userId, null, "TrashEmptied", null, ct);
        await _notifier.NotifyQuotaUpdatedAsync(userId, user.UsedQuotaBytes, user.AllocatedQuotaBytes, ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                action: "TrashEmptied",
                resource: "Trash",
                resourceId: null,
                userId: userId,
                username: user.Username,
                ipAddress: null,
                details: new { deletedFilesCount = deletedFiles.Count, deletedFoldersCount = deletedFolders.Count, freedBytes = freedBytes },
                ct: ct);
        }

        return new EmptyTrashResultDto
        {
            DeletedFilesCount = deletedFiles.Count,
            DeletedFoldersCount = deletedFolders.Count,
            FreedBytes = freedBytes
        };
    }

    public async Task<TrashCleanupResultDto> CleanupExpiredTrashAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - olderThan;

        var expiredFiles = await _dbContext.FileItems
            .Include(f => f.User)
            .Include(f => f.StorageBlob)
                .ThenInclude(b => b.StorageNode)
            .Where(f => f.IsDeleted && ((f.DeletedAt != null && f.DeletedAt <= cutoff) || (f.DeletedAt == null && f.UpdatedAt <= cutoff)))
            .ToListAsync(ct);

        var expiredFolders = await _dbContext.Folders
            .Where(f => f.IsDeleted && ((f.DeletedAt != null && f.DeletedAt <= cutoff) || (f.DeletedAt == null && f.UpdatedAt <= cutoff)))
            .ToListAsync(ct);

        long freedBytes = 0;
        int purgedBlobsCount = 0;
        var removedBlobIds = new HashSet<Guid>();

        foreach (var file in expiredFiles)
        {
            freedBytes += file.SizeBytes;

            if (file.User != null)
            {
                file.User.UsedQuotaBytes = Math.Max(0, file.User.UsedQuotaBytes - file.SizeBytes);
            }

            var blob = file.StorageBlob;
            _dbContext.FileItems.Remove(file);

            if (blob != null)
            {
                blob.ReferenceCount = Math.Max(0, blob.ReferenceCount - 1);
                if (blob.ReferenceCount <= 0 && removedBlobIds.Add(blob.Id))
                {
                    if (blob.StorageNode != null)
                    {
                        var driver = _driverResolver.GetDriverForNode(blob.StorageNode);
                        await driver.DeleteAsync(blob, ct);
                    }
                    _dbContext.StorageBlobs.Remove(blob);
                    purgedBlobsCount++;
                }
            }
        }

        var expiredFolderIds = expiredFolders.Select(f => f.Id).ToHashSet();

        // Null out parent folder ID on any remaining folders referencing an expired folder
        var referencingSubfolders = await _dbContext.Folders
            .Where(f => f.ParentFolderId != null && expiredFolderIds.Contains(f.ParentFolderId.Value) && !expiredFolderIds.Contains(f.Id))
            .ToListAsync(ct);

        foreach (var sub in referencingSubfolders)
        {
            sub.ParentFolderId = null;
        }

        foreach (var folder in expiredFolders)
        {
            folder.ParentFolderId = null;
        }
        await _dbContext.SaveChangesAsync(ct);

        _dbContext.Folders.RemoveRange(expiredFolders);
        await _dbContext.SaveChangesAsync(ct);

        if (expiredFiles.Count > 0)
        {
            var affectedUsers = expiredFiles
                .Where(f => f.User != null)
                .Select(f => f.User!)
                .DistinctBy(u => u.Id);

            foreach (var affectedUser in affectedUsers)
            {
                await _notifier.NotifyQuotaUpdatedAsync(affectedUser.Id, affectedUser.UsedQuotaBytes, affectedUser.AllocatedQuotaBytes, ct);
            }
        }

        return new TrashCleanupResultDto
        {
            PurgedFilesCount = expiredFiles.Count,
            PurgedFoldersCount = expiredFolders.Count,
            FreedBytes = freedBytes,
            PurgedBlobsCount = purgedBlobsCount
        };
    }

    private static HashSet<Guid> GetDescendantFolderIds(Guid rootFolderId, List<Folder> folders)
    {
        var descendants = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(rootFolderId);

        var lookup = folders
            .Where(f => f.ParentFolderId.HasValue)
            .GroupBy(f => f.ParentFolderId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            if (lookup.TryGetValue(currentId, out var children))
            {
                foreach (var child in children)
                {
                    if (child.Id != rootFolderId && descendants.Add(child.Id))
                    {
                        queue.Enqueue(child.Id);
                    }
                }
            }
        }

        return descendants;
    }
}
