using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Trash;
using OmegaCloud.Application.Services;

namespace OmegaCloud.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/trash")]
public class TrashController : ControllerBase
{
    private readonly ITrashService _trashService;
    private readonly ICurrentUser _currentUser;

    public TrashController(
        ITrashService trashService,
        ICurrentUser currentUser)
    {
        _trashService = trashService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [ProducesResponseType(typeof(TrashContentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TrashContentDto>> GetTrash(
        [FromQuery] bool topLevelOnly = false,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var content = await _trashService.GetTrashContentAsync(userId, topLevelOnly, ct);
        return Ok(content);
    }

    [HttpPost("restore/{id:guid}")]
    [ProducesResponseType(typeof(RestoreItemResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RestoreItemResultDto>> RestoreItem(
        Guid id,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var result = await _trashService.RestoreItemAsync(id, userId, ct);
        return Ok(result);
    }

    [HttpDelete("permanent/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PermanentlyDeleteItem(
        Guid id,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        await _trashService.PermanentlyDeleteItemAsync(id, userId, ct);
        return NoContent();
    }

    [HttpDelete("empty")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> EmptyTrash(
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        await _trashService.EmptyTrashAsync(userId, ct);
        return NoContent();
    }

    private Guid GetCurrentUserId()
    {
        return _currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
    }
}
