using Microsoft.EntityFrameworkCore;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Helpers;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Application.DTOs.Folders;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Application.Services;

public class SharedService : ISharedService
{
    private readonly IAppDbContext _dbContext;
    private readonly IStorageDriverResolver _driverResolver;
    private readonly IFileService _fileService;
    private readonly IRealtimeNotifier _notifier;
    private readonly IAuditService? _auditService;

    public SharedService(
        IAppDbContext dbContext,
        IStorageDriverResolver driverResolver,
        IFileService fileService,
        IRealtimeNotifier? notifier = null,
        IAuditService? auditService = null)
    {
        _dbContext = dbContext;
        _driverResolver = driverResolver;
        _fileService = fileService;
        _notifier = notifier ?? NullRealtimeNotifier.Instance;
        _auditService = auditService;
    }

    public async Task<FolderContentDto> GetSharedFolderContentAsync(Guid? folderId, Guid? currentUserId = null, CancellationToken ct = default)
    {
        UserQuotaDto? quotaDto = null;
        if (currentUserId.HasValue)
        {
            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId.Value, ct);
            if (user != null)
            {
                var allocated = user.AllocatedQuotaBytes;
                var used = user.UsedQuotaBytes;
                quotaDto = new UserQuotaDto
                {
                    AllocatedBytes = allocated,
                    UsedBytes = used,
                    AvailableBytes = Math.Max(0, allocated - used)
                };
            }
        }

        if (folderId == null)
        {
            var rootFolders = await _dbContext.Folders
                .AsNoTracking()
                .Where(f => f.IsShared && f.ParentFolderId == null && !f.IsDeleted)
                .OrderBy(f => f.Name)
                .Select(f => MapToDto(f))
                .ToListAsync(ct);

            var rootFiles = await _dbContext.FileItems
                .AsNoTracking()
                .Where(f => f.IsShared && f.FolderId == null && !f.IsDeleted)
                .OrderBy(f => f.Name)
                .Select(f => MapToFileDto(f))
                .ToListAsync(ct);

            return new FolderContentDto
            {
                CurrentFolder = null,
                Breadcrumbs = new List<BreadcrumbDto>
                {
                    new BreadcrumbDto { Id = null, Name = "Shared Space" }
                },
                Folders = rootFolders,
                Files = rootFiles,
                Quota = quotaDto ?? new UserQuotaDto()
            };
        }

        var currentFolder = await _dbContext.Folders
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == folderId.Value && f.IsShared && !f.IsDeleted, ct)
            ?? throw new NotFoundException($"Shared folder with ID '{folderId.Value}' was not found.");

        var breadcrumbs = await BuildSharedBreadcrumbsAsync(currentFolder, ct);

        var subFolders = await _dbContext.Folders
            .AsNoTracking()
            .Where(f => f.IsShared && f.ParentFolderId == folderId.Value && !f.IsDeleted)
            .OrderBy(f => f.Name)
            .Select(f => MapToDto(f))
            .ToListAsync(ct);

        var subFiles = await _dbContext.FileItems
            .AsNoTracking()
            .Where(f => f.IsShared && f.FolderId == folderId.Value && !f.IsDeleted)
            .OrderBy(f => f.Name)
            .Select(f => MapToFileDto(f))
            .ToListAsync(ct);

        return new FolderContentDto
        {
            CurrentFolder = MapToDto(currentFolder),
            Breadcrumbs = breadcrumbs,
            Folders = subFolders,
            Files = subFiles,
            Quota = quotaDto ?? new UserQuotaDto()
        };
    }

    public async Task<FileMetadataDto> GetSharedFileMetadataAsync(Guid fileId, CancellationToken ct = default)
    {
        var file = await _dbContext.FileItems
            .Include(f => f.Folder)
            .Include(f => f.StorageBlob)
                .ThenInclude(b => b.StorageNode)
            .FirstOrDefaultAsync(f => f.Id == fileId && f.IsShared && !f.IsDeleted, ct)
            ?? throw new NotFoundException($"Shared file with ID '{fileId}' was not found.");

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

    public async Task<FileDownloadResult> GetSharedFileDownloadStreamAsync(Guid fileId, CancellationToken ct = default)
    {
        var file = await _dbContext.FileItems
            .Include(f => f.StorageBlob)
                .ThenInclude(b => b.StorageNode)
            .FirstOrDefaultAsync(f => f.Id == fileId && f.IsShared && !f.IsDeleted, ct)
            ?? throw new NotFoundException($"Shared file with ID '{fileId}' was not found.");

        var driver = _driverResolver.GetDriverForNode(file.StorageBlob.StorageNode);
        var directUrl = await driver.TryGenerateDirectDownloadUrlAsync(file.StorageBlob, TimeSpan.FromMinutes(15), ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                action: "SharedFileDownloaded",
                resource: "Files",
                resourceId: file.Id.ToString(),
                userId: null,
                username: "Anonymous",
                ipAddress: null,
                details: new { fileName = file.Name, sizeBytes = file.SizeBytes },
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

    public async Task<FileItemDto> UploadSharedFileAsync(UploadFileStreamRequest request, Guid userId, CancellationToken ct = default)
    {
        request.IsShared = true;

        if (request.FolderId.HasValue)
        {
            var folder = await _dbContext.Folders
                .FirstOrDefaultAsync(f => f.Id == request.FolderId.Value && f.IsShared && !f.IsDeleted, ct)
                ?? throw new NotFoundException($"Shared destination folder with ID '{request.FolderId.Value}' was not found.");
        }

        return await _fileService.UploadFileAsync(request, userId, ct);
    }

    public async Task DeleteSharedFileAsync(Guid fileId, Guid userId, string? userRole, CancellationToken ct = default)
    {
        var file = await _dbContext.FileItems
            .FirstOrDefaultAsync(f => f.Id == fileId && f.IsShared && !f.IsDeleted, ct)
            ?? throw new NotFoundException($"Shared file with ID '{fileId}' was not found.");

        var isAdmin = string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase);
        if (file.UserId != userId && !isAdmin)
        {
            throw new ForbiddenException("You do not have permission to delete this shared file. Only the author or an admin can delete it.");
        }

        file.IsDeleted = true;
        file.DeletedAt = DateTime.UtcNow;
        file.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);

        var dto = MapToFileDto(file);
        await _notifier.NotifyDirectoryChangedAsync(userId, file.FolderId, "Deleted", dto, ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                action: "SharedFileDeleted",
                resource: "Files",
                resourceId: file.Id.ToString(),
                userId: userId,
                username: null,
                ipAddress: null,
                details: new { fileName = file.Name, sizeBytes = file.SizeBytes },
                ct: ct);
        }
    }

    public async Task DeleteSharedFolderAsync(Guid folderId, Guid userId, string? userRole, CancellationToken ct = default)
    {
        var folder = await _dbContext.Folders
            .FirstOrDefaultAsync(f => f.Id == folderId && f.IsShared && !f.IsDeleted, ct)
            ?? throw new NotFoundException($"Shared folder with ID '{folderId}' was not found.");

        var isAdmin = string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase);
        if (folder.UserId != userId && !isAdmin)
        {
            throw new ForbiddenException("You do not have permission to delete this shared folder. Only the author or an admin can delete it.");
        }

        var now = DateTime.UtcNow;
        var descendantFolderIds = new List<Guid> { folderId };
        var queue = new Queue<Guid>();
        queue.Enqueue(folderId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            var children = await _dbContext.Folders
                .Where(f => f.ParentFolderId == currentId && !f.IsDeleted)
                .Select(f => f.Id)
                .ToListAsync(ct);

            foreach (var childId in children)
            {
                descendantFolderIds.Add(childId);
                queue.Enqueue(childId);
            }
        }

        var foldersToSoftDelete = await _dbContext.Folders
            .Where(f => descendantFolderIds.Contains(f.Id) && !f.IsDeleted)
            .ToListAsync(ct);

        foreach (var f in foldersToSoftDelete)
        {
            f.IsDeleted = true;
            f.DeletedAt = now;
            f.UpdatedAt = now;
        }

        var filesToSoftDelete = await _dbContext.FileItems
            .Where(f => f.FolderId.HasValue && descendantFolderIds.Contains(f.FolderId.Value) && !f.IsDeleted)
            .ToListAsync(ct);

        foreach (var f in filesToSoftDelete)
        {
            f.IsDeleted = true;
            f.DeletedAt = now;
            f.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(ct);

        var dto = MapToDto(folder);
        await _notifier.NotifyDirectoryChangedAsync(userId, folder.ParentFolderId, "Deleted", dto, ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                action: "SharedFolderDeleted",
                resource: "Folders",
                resourceId: folder.Id.ToString(),
                userId: userId,
                username: null,
                ipAddress: null,
                details: new { folderName = folder.Name },
                ct: ct);
        }
    }

    public async Task<FolderDto> CreateSharedFolderAsync(CreateFolderRequest request, Guid userId, CancellationToken ct = default)
    {
        var validatedName = FileHelper.ValidateFileName(request.Name);

        if (request.ParentFolderId.HasValue)
        {
            var parentFolder = await _dbContext.Folders
                .FirstOrDefaultAsync(f => f.Id == request.ParentFolderId.Value && f.IsShared && !f.IsDeleted, ct)
                ?? throw new NotFoundException($"Parent shared folder with ID '{request.ParentFolderId.Value}' was not found.");
        }

        var isDuplicate = await _dbContext.Folders
            .AnyAsync(f => f.IsShared && f.ParentFolderId == request.ParentFolderId && f.Name == validatedName && !f.IsDeleted, ct);

        if (isDuplicate)
        {
            throw new ValidationException($"A shared folder named '{validatedName}' already exists in this location.");
        }

        var now = DateTime.UtcNow;
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ParentFolderId = request.ParentFolderId,
            Name = validatedName,
            Color = request.Color?.Trim(),
            IsShared = true,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.Folders.Add(folder);
        await _dbContext.SaveChangesAsync(ct);

        var dto = MapToDto(folder);
        await _notifier.NotifyDirectoryChangedAsync(userId, request.ParentFolderId, "Created", dto, ct);

        return dto;
    }

    private async Task<List<BreadcrumbDto>> BuildSharedBreadcrumbsAsync(Folder folder, CancellationToken ct)
    {
        var breadcrumbs = new List<BreadcrumbDto>
        {
            new BreadcrumbDto { Id = folder.Id, Name = folder.Name }
        };

        var currentParentId = folder.ParentFolderId;
        while (currentParentId.HasValue)
        {
            var parent = await _dbContext.Folders
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == currentParentId.Value && !f.IsDeleted, ct);

            if (parent == null) break;

            breadcrumbs.Insert(0, new BreadcrumbDto { Id = parent.Id, Name = parent.Name });
            currentParentId = parent.ParentFolderId;
        }

        breadcrumbs.Insert(0, new BreadcrumbDto { Id = null, Name = "Shared Space" });
        return breadcrumbs;
    }

    private static FolderDto MapToDto(Folder f) => new FolderDto
    {
        Id = f.Id,
        UserId = f.UserId,
        ParentFolderId = f.ParentFolderId,
        Name = f.Name,
        Color = f.Color,
        IsShared = f.IsShared,
        IsDeleted = f.IsDeleted,
        DeletedAt = f.DeletedAt,
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
        IsShared = f.IsShared,
        IsDeleted = f.IsDeleted,
        DeletedAt = f.DeletedAt,
        IsFavorite = f.IsFavorite,
        CreatedAt = f.CreatedAt,
        UpdatedAt = f.UpdatedAt
    };
}
