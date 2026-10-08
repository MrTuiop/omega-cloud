using Microsoft.EntityFrameworkCore;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Application.DTOs.Folders;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Application.Services;

public class FolderService : IFolderService
{
    private readonly IAppDbContext _dbContext;

    private static readonly char[] InvalidChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0' };

    public FolderService(IAppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<FolderContentDto> GetFolderContentAsync(Guid? folderId, Guid userId, CancellationToken ct = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedException("User not found.");

        var allocated = user.AllocatedQuotaBytes;
        var used = user.UsedQuotaBytes;
        var available = Math.Max(0, allocated - used);

        var quotaDto = new UserQuotaDto
        {
            AllocatedBytes = allocated,
            UsedBytes = used,
            AvailableBytes = available
        };

        if (folderId == null)
        {
            var rootFolders = await _dbContext.Folders
                .AsNoTracking()
                .Where(f => f.UserId == userId && f.ParentFolderId == null && !f.IsDeleted)
                .OrderBy(f => f.Name)
                .Select(f => MapToDto(f))
                .ToListAsync(ct);

            var rootFiles = await _dbContext.FileItems
                .AsNoTracking()
                .Where(f => f.UserId == userId && f.FolderId == null && !f.IsDeleted)
                .OrderBy(f => f.Name)
                .Select(f => MapToFileDto(f))
                .ToListAsync(ct);

            return new FolderContentDto
            {
                CurrentFolder = null,
                Breadcrumbs = new List<BreadcrumbDto>
                {
                    new BreadcrumbDto { Id = null, Name = "Root" }
                },
                Folders = rootFolders,
                Files = rootFiles,
                Quota = quotaDto
            };
        }

        var currentFolder = await _dbContext.Folders
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == folderId.Value && f.UserId == userId && !f.IsDeleted, ct)
            ?? throw new NotFoundException($"Folder with ID '{folderId.Value}' was not found.");

        var breadcrumbs = await BuildBreadcrumbsAsync(currentFolder, userId, ct);

        var subFolders = await _dbContext.Folders
            .AsNoTracking()
            .Where(f => f.UserId == userId && f.ParentFolderId == folderId.Value && !f.IsDeleted)
            .OrderBy(f => f.Name)
            .Select(f => MapToDto(f))
            .ToListAsync(ct);

        var subFiles = await _dbContext.FileItems
            .AsNoTracking()
            .Where(f => f.UserId == userId && f.FolderId == folderId.Value && !f.IsDeleted)
            .OrderBy(f => f.Name)
            .Select(f => MapToFileDto(f))
            .ToListAsync(ct);

        return new FolderContentDto
        {
            CurrentFolder = MapToDto(currentFolder),
            Breadcrumbs = breadcrumbs,
            Folders = subFolders,
            Files = subFiles,
            Quota = quotaDto
        };
    }

    public async Task<FolderDto> CreateFolderAsync(CreateFolderRequest request, Guid userId, CancellationToken ct = default)
    {
        var validatedName = ValidateFolderName(request.Name);

        if (request.ParentFolderId.HasValue)
        {
            var parentExists = await _dbContext.Folders
                .AnyAsync(f => f.Id == request.ParentFolderId.Value && f.UserId == userId && !f.IsDeleted, ct);

            if (!parentExists)
            {
                throw new NotFoundException($"Parent folder with ID '{request.ParentFolderId.Value}' was not found.");
            }
        }

        var isDuplicate = await _dbContext.Folders
            .AnyAsync(f => f.UserId == userId && f.ParentFolderId == request.ParentFolderId && f.Name == validatedName && !f.IsDeleted, ct);

        if (isDuplicate)
        {
            throw new ValidationException($"A folder named '{validatedName}' already exists in this location.");
        }

        var now = DateTime.UtcNow;
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ParentFolderId = request.ParentFolderId,
            Name = validatedName,
            Color = request.Color?.Trim(),
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.Folders.Add(folder);
        await _dbContext.SaveChangesAsync(ct);

        return MapToDto(folder);
    }

    public async Task<FolderDto> UpdateFolderAsync(Guid id, UpdateFolderRequest request, Guid userId, CancellationToken ct = default)
    {
        var folder = await _dbContext.Folders
            .FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId && !f.IsDeleted, ct)
            ?? throw new NotFoundException($"Folder with ID '{id}' was not found.");

        var targetParentId = folder.ParentFolderId;
        var isMoving = false;

        if (request.MoveToRoot == true)
        {
            targetParentId = null;
            isMoving = true;
        }
        else if (request.ParentFolderId.HasValue && request.ParentFolderId.Value != folder.ParentFolderId)
        {
            targetParentId = request.ParentFolderId.Value;
            isMoving = true;
        }

        var targetName = folder.Name;
        var isRenaming = false;
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var newName = ValidateFolderName(request.Name);
            if (!string.Equals(newName, folder.Name, StringComparison.Ordinal))
            {
                targetName = newName;
                isRenaming = true;
            }
        }

        if (isMoving && targetParentId.HasValue)
        {
            if (targetParentId.Value == folder.Id)
            {
                throw new ValidationException("A folder cannot be moved into itself.");
            }

            var destFolderExists = await _dbContext.Folders
                .AnyAsync(f => f.Id == targetParentId.Value && f.UserId == userId && !f.IsDeleted, ct);

            if (!destFolderExists)
            {
                throw new NotFoundException($"Destination folder with ID '{targetParentId.Value}' was not found.");
            }

            // Prevent cyclic references: targetParent must not be a descendant of this folder
            var allUserFolders = await _dbContext.Folders
                .AsNoTracking()
                .Where(f => f.UserId == userId && !f.IsDeleted)
                .ToListAsync(ct);

            var descendants = GetDescendantFolderIds(folder.Id, allUserFolders);
            if (descendants.Contains(targetParentId.Value))
            {
                throw new ValidationException("A folder cannot be moved into one of its own subfolders.");
            }
        }

        if (isRenaming || isMoving)
        {
            var isDuplicate = await _dbContext.Folders
                .AnyAsync(f => f.UserId == userId && f.ParentFolderId == targetParentId && f.Name == targetName && f.Id != folder.Id && !f.IsDeleted, ct);

            if (isDuplicate)
            {
                throw new ValidationException($"A folder named '{targetName}' already exists in the destination folder.");
            }
        }

        if (isRenaming)
        {
            folder.Name = targetName;
        }

        if (isMoving)
        {
            folder.ParentFolderId = targetParentId;
        }

        if (request.Color != null)
        {
            folder.Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim();
        }

        folder.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        return MapToDto(folder);
    }

    public async Task DeleteFolderAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var folder = await _dbContext.Folders
            .FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId && !f.IsDeleted, ct)
            ?? throw new NotFoundException($"Folder with ID '{id}' was not found.");

        var now = DateTime.UtcNow;
        folder.IsDeleted = true;
        folder.DeletedAt = now;
        folder.UpdatedAt = now;

        var allUserFolders = await _dbContext.Folders
            .Where(f => f.UserId == userId && !f.IsDeleted && f.Id != id)
            .ToListAsync(ct);

        var descendantIds = GetDescendantFolderIds(folder.Id, allUserFolders);

        foreach (var sub in allUserFolders.Where(f => descendantIds.Contains(f.Id)))
        {
            sub.IsDeleted = true;
            sub.DeletedAt = now;
            sub.UpdatedAt = now;
        }

        var affectedFolderIds = descendantIds.Concat(new[] { folder.Id }).ToList();

        var affectedFiles = await _dbContext.FileItems
            .Where(f => f.UserId == userId && f.FolderId != null && affectedFolderIds.Contains(f.FolderId.Value) && !f.IsDeleted)
            .ToListAsync(ct);

        foreach (var file in affectedFiles)
        {
            file.IsDeleted = true;
            file.DeletedAt = now;
            file.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    private static string ValidateFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("Folder name cannot be empty.");
        }

        var trimmed = name.Trim();

        if (trimmed.Length > 255)
        {
            throw new ValidationException("Folder name cannot exceed 255 characters.");
        }

        if (trimmed == "." || trimmed == "..")
        {
            throw new ValidationException("Invalid folder name.");
        }

        if (trimmed.IndexOfAny(InvalidChars) >= 0)
        {
            throw new ValidationException("Folder name contains invalid characters.");
        }

        return trimmed;
    }

    private async Task<List<BreadcrumbDto>> BuildBreadcrumbsAsync(Folder folder, Guid userId, CancellationToken ct)
    {
        var breadcrumbs = new List<BreadcrumbDto>
        {
            new BreadcrumbDto { Id = folder.Id, Name = folder.Name }
        };

        var currentParentId = folder.ParentFolderId;
        var visited = new HashSet<Guid> { folder.Id };

        while (currentParentId.HasValue && visited.Add(currentParentId.Value))
        {
            var parent = await _dbContext.Folders
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == currentParentId.Value && f.UserId == userId && !f.IsDeleted, ct);

            if (parent == null)
            {
                break;
            }

            breadcrumbs.Insert(0, new BreadcrumbDto { Id = parent.Id, Name = parent.Name });
            currentParentId = parent.ParentFolderId;
        }

        breadcrumbs.Insert(0, new BreadcrumbDto { Id = null, Name = "Root" });
        return breadcrumbs;
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
                    if (descendants.Add(child.Id))
                    {
                        queue.Enqueue(child.Id);
                    }
                }
            }
        }

        return descendants;
    }

    private static FolderDto MapToDto(Folder f) => new FolderDto
    {
        Id = f.Id,
        UserId = f.UserId,
        ParentFolderId = f.ParentFolderId,
        Name = f.Name,
        Color = f.Color,
        CreatedAt = f.CreatedAt,
        UpdatedAt = f.UpdatedAt
    };

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
        IsFavorite = f.IsFavorite,
        CreatedAt = f.CreatedAt,
        UpdatedAt = f.UpdatedAt
    };
}
