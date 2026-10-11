using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Helpers;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Application.DTOs.Folders;
using OmegaCloud.Application.DTOs.Upload;
using OmegaCloud.Application.Services;

namespace OmegaCloud.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/upload")]
public class UploadController : ControllerBase
{
    private const long MaxDirectStreamSizeBytes = 50 * 1024 * 1024; // 50 MB

    private readonly IUploadService _uploadService;
    private readonly ICurrentUser _currentUser;
    private readonly IAppDbContext _dbContext;

    public UploadController(
        IUploadService uploadService,
        ICurrentUser currentUser,
        IAppDbContext dbContext)
    {
        _uploadService = uploadService;
        _currentUser = currentUser;
        _dbContext = dbContext;
    }

    [HttpPost("direct-stream")]
    [DisableRequestSizeLimit]
    [ProducesResponseType(typeof(FileItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FileItemDto>> DirectStreamUpload(
        [FromQuery] Guid? folderId,
        [FromQuery] string? fileName,
        [FromQuery] Guid? storageNodeId,
        [FromQuery] Guid? nodeId,
        [FromQuery] OmegaCloud.Domain.Enums.ConflictResolutionStrategy? conflictResolution,
        [FromQuery] OmegaCloud.Domain.Enums.ConflictResolutionStrategy? conflictStrategy,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var effectiveNodeId = storageNodeId ?? nodeId;
        var effectiveConflict = conflictResolution ?? conflictStrategy;
        if (!effectiveConflict.HasValue && Request.Headers.TryGetValue("X-Conflict-Resolution", out var hConflict))
        {
            if (Enum.TryParse<OmegaCloud.Domain.Enums.ConflictResolutionStrategy>(hConflict.ToString(), true, out var parsedConflict))
            {
                effectiveConflict = parsedConflict;
            }
        }

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(ct);
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
            {
                throw new ValidationException("No file was provided for upload.");
            }

            if (file.Length > MaxDirectStreamSizeBytes)
            {
                throw new ValidationException($"File size exceeds the 50 MB limit for direct streaming ({MaxDirectStreamSizeBytes} bytes). Use chunked upload sessions for files larger than 50 MB.");
            }

            Guid? targetFolderId = folderId;
            if (form.TryGetValue("folderId", out var formFolderVal) && Guid.TryParse(formFolderVal, out var parsedGuid))
            {
                targetFolderId = parsedGuid;
            }

            if (!effectiveConflict.HasValue && form.TryGetValue("conflictResolution", out var formConfVal))
            {
                if (Enum.TryParse<OmegaCloud.Domain.Enums.ConflictResolutionStrategy>(formConfVal.ToString(), true, out var parsedConf))
                {
                    effectiveConflict = parsedConf;
                }
            }

            if (!effectiveNodeId.HasValue)
            {
                if (form.TryGetValue("storageNodeId", out var formNodeVal) && Guid.TryParse(formNodeVal, out var parsedNodeGuid))
                {
                    effectiveNodeId = parsedNodeGuid;
                }
                else if (form.TryGetValue("nodeId", out var formNVal) && Guid.TryParse(formNVal, out var parsedNVal))
                {
                    effectiveNodeId = parsedNVal;
                }
            }

            using var contentStream = file.OpenReadStream();
            var effectiveMultipartFileName = !string.IsNullOrWhiteSpace(fileName) ? fileName : file.FileName;
            var request = new UploadFileStreamRequest
            {
                Content = contentStream,
                FileName = effectiveMultipartFileName,
                ContentType = file.ContentType,
                SizeBytes = file.Length,
                FolderId = targetFolderId,
                StorageNodeId = effectiveNodeId,
                ConflictResolution = effectiveConflict
            };

            var result = await _uploadService.DirectStreamUploadAsync(request, userId, ct);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        else
        {
            // Stream directly using PipeReader
            var effectiveFileName = fileName;
            if (string.IsNullOrWhiteSpace(effectiveFileName) && Request.Headers.TryGetValue("X-File-Name", out var hFileName))
            {
                effectiveFileName = Uri.UnescapeDataString(hFileName.ToString());
            }

            if (string.IsNullOrWhiteSpace(effectiveFileName) && !string.IsNullOrWhiteSpace(Request.Headers.ContentDisposition))
            {
                if (System.Net.Http.Headers.ContentDispositionHeaderValue.TryParse(Request.Headers.ContentDisposition, out var cd))
                {
                    if (!string.IsNullOrWhiteSpace(cd.FileNameStar))
                    {
                        effectiveFileName = cd.FileNameStar.Trim('"');
                    }
                    else if (!string.IsNullOrWhiteSpace(cd.FileName))
                    {
                        effectiveFileName = cd.FileName.Trim('"');
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(effectiveFileName))
            {
                throw new ValidationException("File name must be specified via query parameter 'fileName', header 'X-File-Name', or 'Content-Disposition'.");
            }

            Guid? targetFolderId = folderId;
            if (Request.Headers.TryGetValue("X-Folder-Id", out var hFolderVal) && Guid.TryParse(hFolderVal, out var parsedHGuid))
            {
                targetFolderId = parsedHGuid;
            }

            if (!effectiveNodeId.HasValue)
            {
                if (Request.Headers.TryGetValue("X-Storage-Node-Id", out var hNodeVal) && Guid.TryParse(hNodeVal, out var parsedNodeHGuid))
                {
                    effectiveNodeId = parsedNodeHGuid;
                }
                else if (Request.Headers.TryGetValue("X-Node-Id", out var hNVal) && Guid.TryParse(hNVal, out var parsedNHGuid))
                {
                    effectiveNodeId = parsedNHGuid;
                }
            }

            if (Request.ContentLength.HasValue)
            {
                if (Request.ContentLength.Value <= 0)
                {
                    throw new ValidationException("Cannot upload an empty file.");
                }

                if (Request.ContentLength.Value > MaxDirectStreamSizeBytes)
                {
                    throw new ValidationException($"File size exceeds the 50 MB limit for direct streaming ({MaxDirectStreamSizeBytes} bytes). Use chunked upload sessions for files larger than 50 MB.");
                }
            }

            var bodyStream = Request.BodyReader.AsStream();
            using var limitingStream = new SizeLimitingStream(bodyStream, MaxDirectStreamSizeBytes);

            var contentType = string.IsNullOrWhiteSpace(Request.ContentType)
                ? "application/octet-stream"
                : Request.ContentType;

            long? sizeBytes = Request.ContentLength;

            var request = new UploadFileStreamRequest
            {
                Content = limitingStream,
                FileName = effectiveFileName,
                ContentType = contentType,
                SizeBytes = sizeBytes,
                FolderId = targetFolderId,
                StorageNodeId = effectiveNodeId,
                ConflictResolution = effectiveConflict
            };

            var result = await _uploadService.DirectStreamUploadAsync(request, userId, ct);
            return StatusCode(StatusCodes.Status201Created, result);
        }
    }

    [HttpPost("sessions/init")]
    [HttpPost("sessions")]
    [ProducesResponseType(typeof(UploadSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UploadSessionDto>> InitSession(
        [FromBody] InitUploadSessionRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var result = await _uploadService.InitSessionAsync(request, userId, ct);
        return Ok(result);
    }

    [HttpPut("sessions/{id:guid}/chunks/{index:int}")]
    [HttpPost("sessions/{id:guid}/chunks/{index:int}")]
    [DisableRequestSizeLimit]
    [ProducesResponseType(typeof(UploadChunkResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UploadChunkResultDto>> UploadChunk(
        Guid id,
        int index,
        [FromQuery] string? sha256,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();

        var expectedSha = sha256;
        if (string.IsNullOrWhiteSpace(expectedSha) && Request.Headers.TryGetValue("X-Chunk-Sha256", out var hSha))
        {
            expectedSha = hSha.ToString();
        }

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(ct);
            if (string.IsNullOrWhiteSpace(expectedSha) && form.TryGetValue("sha256", out var formSha))
            {
                expectedSha = formSha.ToString();
            }
            if (string.IsNullOrWhiteSpace(expectedSha) && form.TryGetValue("chunkSha256", out var formCSha))
            {
                expectedSha = formCSha.ToString();
            }

            var file = form.Files["chunk"] ?? form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
            {
                throw new ValidationException("No chunk content provided.");
            }

            using var stream = file.OpenReadStream();
            var result = await _uploadService.UploadChunkAsync(id, index, stream, file.Length, expectedSha, userId, ct);
            return Ok(result);
        }
        else
        {
            if (Request.ContentLength.HasValue && Request.ContentLength.Value <= 0)
            {
                throw new ValidationException("Chunk content cannot be empty.");
            }

            var bodyStream = Request.BodyReader.AsStream();
            var result = await _uploadService.UploadChunkAsync(id, index, bodyStream, Request.ContentLength, expectedSha, userId, ct);
            return Ok(result);
        }
    }

    [HttpPost("sessions/{id:guid}/complete")]
    [ProducesResponseType(typeof(FileItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FileItemDto>> CompleteSession(
        Guid id,
        [FromBody] CompleteUploadSessionRequest? request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var result = await _uploadService.CompleteSessionAsync(id, request, userId, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("sessions/{id:guid}")]
    [ProducesResponseType(typeof(UploadSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UploadSessionDto>> GetSession(
        Guid id,
        [FromQuery] int? chunkIndex,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var result = await _uploadService.GetSessionStatusAsync(id, userId, chunkIndex ?? 0, ct);
        return Ok(result);
    }

    [HttpDelete("sessions/{id:guid}")]
    [HttpPost("sessions/{id:guid}/abort")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AbortSession(
        Guid id,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await _uploadService.AbortSessionAsync(id, userId, ct);
        return NoContent();
    }

    [HttpGet("quota")]
    [ProducesResponseType(typeof(UserQuotaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserQuotaDto>> GetUserQuota(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedException("User not found.");

        var allocated = user.AllocatedQuotaBytes;
        var used = user.UsedQuotaBytes;
        var available = Math.Max(0, allocated - used);

        return Ok(new UserQuotaDto
        {
            AllocatedBytes = allocated,
            UsedBytes = used,
            AvailableBytes = available
        });
    }

    private Guid GetCurrentUserId()
    {
        return _currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
    }
}
