using Microsoft.EntityFrameworkCore;
using OmegaCloud.Application.Common.Exceptions;
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

    private static readonly char[] InvalidChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0' };

    public FileService(
        IAppDbContext dbContext,
        IStorageDriverResolver driverResolver)
    {
        _dbContext = dbContext;
        _driverResolver = driverResolver;
    }

    public async Task<FileItemDto> UploadFileAsync(UploadFileStreamRequest request, Guid userId, CancellationToken ct = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedException("User not found.");

        if (request.SizeBytes <= 0)
        {
            throw new ValidationException("Cannot upload an empty file.");
        }

        if (user.UsedQuotaBytes + request.SizeBytes > user.AllocatedQuotaBytes)
        {
            var remainingQuota = Math.Max(0, user.AllocatedQuotaBytes - user.UsedQuotaBytes);
            throw new ValidationException($"User quota exceeded. Available: {remainingQuota} bytes, File size: {request.SizeBytes} bytes.");
        }

        var validatedFileName = ValidateFileName(request.FileName);

        if (request.FolderId.HasValue)
        {
            var folderExists = await _dbContext.Folders
                .AnyAsync(f => f.Id == request.FolderId.Value && f.UserId == userId && !f.IsDeleted, ct);

            if (!folderExists)
            {
                throw new NotFoundException($"Destination folder with ID '{request.FolderId.Value}' was not found.");
            }
        }

        var isDuplicate = await _dbContext.FileItems
            .AnyAsync(f => f.UserId == userId && f.FolderId == request.FolderId && f.Name == validatedFileName && !f.IsDeleted, ct);

        if (isDuplicate)
        {
            throw new ValidationException($"A file named '{validatedFileName}' already exists in this folder.");
        }

        var localStorageNode = await _dbContext.StorageNodes
            .FirstOrDefaultAsync(n => n.Type == StorageNodeType.LocalDisk && n.Status == StorageNodeStatus.Online, ct);

        if (localStorageNode == null)
        {
            localStorageNode = await _dbContext.StorageNodes
                .FirstOrDefaultAsync(n => n.Type == StorageNodeType.LocalDisk, ct);

            if (localStorageNode == null)
            {
                throw new InvalidOperationException("No local storage node configured.");
            }
        }

        var driver = _driverResolver.GetDriverForNode(localStorageNode);

        var writeResult = await driver.WriteAsync(
            request.Content,
            new StorageWriteOptions
            {
                ExpectedSize = request.SizeBytes,
                ContentType = request.ContentType
            },
            ct);

        // Deduplication check: Has this exact blob already been stored on this node?
        var existingBlob = await _dbContext.StorageBlobs
            .FirstOrDefaultAsync(b => b.StorageNodeId == localStorageNode.Id &&
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
                StorageNodeId = localStorageNode.Id,
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
            IsDeleted = false,
            IsFavorite = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.FileItems.Add(fileItem);
        user.UsedQuotaBytes += writeResult.SizeBytes;

        await _dbContext.SaveChangesAsync(ct);

        return MapToFileDto(fileItem);
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

        return MapToFileDto(file);
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
    }

    private static string ValidateFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("File name cannot be empty.");
        }

        var sanitized = Path.GetFileName(name).Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new ValidationException("Invalid file name.");
        }

        if (sanitized.Length > 255)
        {
            throw new ValidationException("File name cannot exceed 255 characters.");
        }

        if (sanitized == "." || sanitized == "..")
        {
            throw new ValidationException("Invalid file name.");
        }

        if (sanitized.IndexOfAny(InvalidChars) >= 0)
        {
            throw new ValidationException("File name contains invalid characters.");
        }

        return sanitized;
    }

    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".txt" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mkv" => "video/x-matroska",
            ".zip" => "application/zip",
            ".tar" => "application/x-tar",
            ".gz" => "application/gzip",
            _ => "application/octet-stream"
        };
    }

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
