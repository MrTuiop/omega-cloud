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
[Route("api/v1/upload")]
public class UploadController : ControllerBase
{
    private readonly IFileService _fileService;
    private readonly ICurrentUser _currentUser;

    public UploadController(
        IFileService fileService,
        ICurrentUser currentUser)
    {
        _fileService = fileService;
        _currentUser = currentUser;
    }

    [HttpPost("direct-stream")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FileItemDto>> DirectStreamUpload(
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
        return StatusCode(StatusCodes.Status201Created, result);
    }

    private Guid GetCurrentUserId()
    {
        return _currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
    }
}
