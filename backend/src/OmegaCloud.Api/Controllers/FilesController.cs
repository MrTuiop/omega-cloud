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
            FolderId = folderId
        };

        var result = await _fileService.UploadFileAsync(request, userId, ct);
        return CreatedAtAction(nameof(GetMetadata), new { id = result.Id }, result);
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

        return File(result.Stream!, result.ContentType, result.FileName, enableRangeProcessing: true);
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

    private Guid GetCurrentUserId()
    {
        return _currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
    }
}
