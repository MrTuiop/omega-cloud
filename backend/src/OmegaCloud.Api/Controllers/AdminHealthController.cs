using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmegaCloud.Application.DTOs.Admin;
using OmegaCloud.Application.Services;

namespace OmegaCloud.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/v1/admin/health")]
public class AdminHealthController : ControllerBase
{
    private readonly ISystemHealthService _healthService;

    public AdminHealthController(ISystemHealthService healthService)
    {
        _healthService = healthService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(SystemHealthDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SystemHealthDto>> GetHealth(CancellationToken ct = default)
    {
        var health = await _healthService.GetSystemHealthAsync(ct);
        return Ok(health);
    }
}
