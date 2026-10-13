using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Application.DTOs.Folders;
using OmegaCloud.Application.Services;
using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Api.Controllers;

[ApiController]
[Route("api/v1/shared")]
public class SharedController : ControllerBase
{
    private readonly ISharedService _sharedService;
    private readonly IFileService _fileService;
    private readonly ICurrentUser _currentUser;

    public SharedController(
        ISharedService sharedService,
        IFileService fileService,
        ICurrentUser currentUser)
    {
        _sharedService = sharedService;
        _fileService = fileService;
        _currentUser = currentUser;
    }

    [HttpGet("folders")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FolderContentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FolderContentDto>> GetSharedFolders(
        [FromQuery] Guid? folderId,
        CancellationToken ct = default)
    {
        var currentUserId = _currentUser.UserId;
        var result = await _sharedService.GetSharedFolderContentAsync(folderId, currentUserId, ct);
        return Ok(result);
    }

    [HttpGet("folders/{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FolderContentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FolderContentDto>> GetSharedFolder(
        Guid id,
        CancellationToken ct = default)
    {
        var currentUserId = _currentUser.UserId;
        var result = await _sharedService.GetSharedFolderContentAsync(id, currentUserId, ct);
        return Ok(result);
    }

    [HttpPost("folders")]
    [Authorize]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FolderDto>> CreateSharedFolder(
        [FromBody] CreateFolderRequest request,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        request.IsShared = true;
        var result = await _sharedService.CreateSharedFolderAsync(request, userId, ct);
        return CreatedAtAction(nameof(GetSharedFolder), new { id = result.Id }, result);
    }

    [HttpGet("files/{id:guid}/metadata")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FileMetadataDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FileMetadataDto>> GetSharedFileMetadata(
        Guid id,
        CancellationToken ct = default)
    {
        var metadata = await _sharedService.GetSharedFileMetadataAsync(id, ct);
        return Ok(metadata);
    }

    [HttpGet("files/{id:guid}/download")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadSharedFile(
        Guid id,
        [FromQuery] bool json = false,
        [FromQuery] bool inline = false,
        CancellationToken ct = default)
    {
        var result = await _sharedService.GetSharedFileDownloadStreamAsync(id, ct);

        if (result.IsRedirect)
        {
            if (json)
            {
                return Ok(new { redirectUrl = result.RedirectUrl });
            }
            return Redirect(result.RedirectUrl!);
        }

        if (inline)
        {
            Response.Headers.AcceptRanges = "bytes";
            return File(result.Stream!, result.ContentType, enableRangeProcessing: true);
        }

        return File(result.Stream!, result.ContentType, result.FileName, enableRangeProcessing: true);
    }

    [HttpGet("files/{id:guid}/thumbnail")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSharedFileThumbnail(
        Guid id,
        [FromQuery] int width = 256,
        [FromQuery] int height = 256,
        [FromQuery] string format = "webp",
        CancellationToken ct = default)
    {
        var result = await _fileService.GetFileThumbnailAsync(id, _currentUser.UserId, width, height, format, ct);

        if (!string.IsNullOrEmpty(result.ETag))
        {
            Response.Headers.ETag = result.ETag;
            Response.Headers.CacheControl = "public, max-age=86400";
        }

        return File(result.Stream, result.ContentType);
    }

    [HttpGet("files/{id:guid}/stream")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StreamSharedFile(Guid id, CancellationToken ct = default)
    {
        var result = await _sharedService.GetSharedFileDownloadStreamAsync(id, ct);

        if (result.IsRedirect)
        {
            return Redirect(result.RedirectUrl!);
        }

        Response.Headers.AcceptRanges = "bytes";
        return File(result.Stream!, result.ContentType, enableRangeProcessing: true);
    }

    [HttpPost("upload")]
    [Authorize]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FileItemDto>> UploadSharedFile(
        IFormFile file,
        [FromForm] Guid? folderId,
        [FromForm] Guid? storageNodeId,
        [FromQuery] Guid? nodeId,
        [FromForm] ConflictResolutionStrategy? conflictResolution,
        [FromQuery] ConflictResolutionStrategy? conflictStrategy,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();

        if (file == null || file.Length == 0)
        {
            throw new ValidationException("No file was provided for upload.");
        }

        using var contentStream = file.OpenReadStream();

        var request = new UploadFileStreamRequest
        {
            Content = contentStream,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            FolderId = folderId,
            StorageNodeId = storageNodeId ?? nodeId,
            IsShared = true,
            ConflictResolution = conflictResolution ?? conflictStrategy
        };

        var result = await _sharedService.UploadSharedFileAsync(request, userId, ct);
        return CreatedAtAction(nameof(GetSharedFileMetadata), new { id = result.Id }, result);
    }

    [HttpDelete("files/{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSharedFile(Guid id, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var userRole = _currentUser.Role;
        await _sharedService.DeleteSharedFileAsync(id, userId, userRole, ct);
        return NoContent();
    }

    [HttpDelete("folders/{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSharedFolder(Guid id, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var userRole = _currentUser.Role;
        await _sharedService.DeleteSharedFolderAsync(id, userId, userRole, ct);
        return NoContent();
    }

    [HttpPost("save-to-personal")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveToPersonal(
        [FromBody] SaveToPersonalRequest request,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var targetFolderId = request.TargetFolderId ?? request.TargetSharedFolderId;
        await _sharedService.SaveToPersonalAsync(request.FileIds, request.FolderIds, targetFolderId, userId, ct);
        return Ok(new { message = "Files saved to personal space successfully." });
    }

    [HttpPost("copy-to-shared")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CopyToShared(
        [FromBody] CopyToSharedRequest request,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var targetFolderId = request.TargetSharedFolderId ?? request.TargetFolderId;
        await _sharedService.CopyToSharedAsync(request.FileIds, request.FolderIds, targetFolderId, userId, ct);
        return Ok(new { message = "Files copied to shared space successfully." });
    }


    private Guid GetCurrentUserId()
    {
        return _currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
    }
}
