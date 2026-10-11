using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Application.Services;

namespace OmegaCloud.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/files")]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;
    private readonly ICurrentUser _currentUser;

    public FilesController(
        IFileService fileService,
        ICurrentUser currentUser)
    {
        _fileService = fileService;
        _currentUser = currentUser;
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FileItemDto>> UploadFile(
        IFormFile file,
        [FromForm] Guid? folderId,
        [FromForm] Guid? storageNodeId,
        [FromQuery] Guid? nodeId,
        [FromForm] OmegaCloud.Domain.Enums.ConflictResolutionStrategy? conflictResolution,
        [FromQuery] OmegaCloud.Domain.Enums.ConflictResolutionStrategy? conflictStrategy,
        CancellationToken ct)
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
            ConflictResolution = conflictResolution ?? conflictStrategy
        };

        var result = await _fileService.UploadFileAsync(request, userId, ct);
        return CreatedAtAction(nameof(GetMetadata), new { id = result.Id }, result);
    }

    [HttpPost("check-conflicts")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CheckConflicts(
        [FromBody] CheckFileConflictsRequest request,
        [FromQuery] bool details = false,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (details)
        {
            var conflictDetails = await _fileService.CheckConflictDetailsAsync(request, userId, ct);
            return Ok(conflictDetails);
        }

        var conflicts = await _fileService.CheckConflictsAsync(request, userId, ct);
        return Ok(conflicts);
    }

    [HttpGet("recent")]
    [ProducesResponseType(typeof(IReadOnlyList<FileItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<FileItemDto>>> GetRecentFiles(
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var files = await _fileService.GetRecentFilesAsync(userId, limit, ct);
        return Ok(files);
    }

    [HttpGet("search")]
    [ProducesResponseType(typeof(OmegaCloud.Application.DTOs.Folders.FolderContentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<OmegaCloud.Application.DTOs.Folders.FolderContentDto>> SearchFiles(
        [FromQuery] string q,
        [FromQuery] Guid? folderId = null,
        [FromQuery] string scope = "everywhere",
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var result = await _fileService.SearchAsync(q, folderId, scope, userId, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}/metadata")]
    [ProducesResponseType(typeof(FileMetadataDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FileMetadataDto>> GetMetadata(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var metadata = await _fileService.GetFileMetadataAsync(id, userId, ct);
        return Ok(metadata);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FileMetadataDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FileMetadataDto>> GetFile(Guid id, CancellationToken ct)
    {
        return await GetMetadata(id, ct);
    }

    [HttpGet("{id:guid}/download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadFile(
        Guid id,
        [FromQuery] bool json = false,
        [FromQuery] bool inline = false,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var result = await _fileService.GetFileDownloadStreamAsync(id, userId, ct);

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

    [HttpGet("{id:guid}/thumbnail")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetThumbnail(
        Guid id,
        [FromQuery] int width = 256,
        [FromQuery] int height = 256,
        [FromQuery] string format = "webp",
        CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        var result = await _fileService.GetFileThumbnailAsync(id, userId, width, height, format, ct);

        if (!string.IsNullOrEmpty(result.ETag))
        {
            Response.Headers.ETag = result.ETag;
            Response.Headers.CacheControl = "public, max-age=86400";
        }

        return File(result.Stream, result.ContentType);
    }

    [HttpGet("{id:guid}/stream")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StreamFile(Guid id, CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        var result = await _fileService.GetFileStreamAsync(id, userId, ct);

        if (result.IsRedirect)
        {
            return Redirect(result.RedirectUrl!);
        }

        Response.Headers.AcceptRanges = "bytes";
        return File(result.Stream!, result.ContentType, enableRangeProcessing: true);
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(FileItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FileItemDto>> UpdateFile(
        Guid id,
        [FromBody] UpdateFileRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var result = await _fileService.UpdateFileAsync(id, request, userId, ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFile(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await _fileService.DeleteFileAsync(id, userId, ct);
        return NoContent();
    }

    [HttpPost("download-zip")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadZip(
        [FromBody] DownloadZipRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var result = await _fileService.DownloadFilesZipAsync(request.FileIds, request.FolderIds, userId, ct);
        return File(result.Stream!, result.ContentType, result.FileName);
    }

    [HttpPost("bulk-delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> BulkDelete(
        [FromBody] BulkDeleteRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await _fileService.BulkDeleteAsync(request.FileIds, request.FolderIds, userId, ct);
        return NoContent();
    }

    [HttpPost("bulk-move")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BulkMove(
        [FromBody] BulkMoveRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await _fileService.BulkMoveAsync(request.FileIds, request.FolderIds, request.TargetFolderId, userId, ct);
        return NoContent();
    }

    private Guid GetCurrentUserId()
    {
        return _currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
    }
}
