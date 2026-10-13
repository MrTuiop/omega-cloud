using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Folders;
using OmegaCloud.Application.Services;

namespace OmegaCloud.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/folders")]
public class FoldersController : ControllerBase
{
    private readonly IFolderService _folderService;
    private readonly ICurrentUser _currentUser;

    public FoldersController(
        IFolderService folderService,
        ICurrentUser currentUser)
    {
        _folderService = folderService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [ProducesResponseType(typeof(FolderContentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<FolderContentDto>> GetRootFolderContent(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var content = await _folderService.GetFolderContentAsync(null, userId, ct);
        return Ok(content);
    }

    [HttpGet("tree")]
    [ProducesResponseType(typeof(FolderTreeResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<FolderTreeResponseDto>> GetFolderTree(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var tree = await _folderService.GetFolderTreeAsync(userId, ct);
        return Ok(tree);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FolderContentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FolderContentDto>> GetFolderContent(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var content = await _folderService.GetFolderContentAsync(id, userId, ct);
        return Ok(content);
    }

    [HttpPost]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FolderDto>> CreateFolder(
        [FromBody] CreateFolderRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var result = await _folderService.CreateFolderAsync(request, userId, ct);
        return CreatedAtAction(nameof(GetFolderContent), new { id = result.Id }, result);
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FolderDto>> UpdateFolder(
        Guid id,
        [FromBody] UpdateFolderRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var result = await _folderService.UpdateFolderAsync(id, request, userId, ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFolder(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await _folderService.DeleteFolderAsync(id, userId, ct);
        return NoContent();
    }

    private Guid GetCurrentUserId()
    {
        return _currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
    }
}
