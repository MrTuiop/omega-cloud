using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmegaCloud.Application.DTOs.Nodes;
using OmegaCloud.Application.Services;

namespace OmegaCloud.Api.Controllers;

[ApiController]
[Route("api/v1/nodes")]
public class NodesController : ControllerBase
{
    private readonly INodeService _nodeService;

    public NodesController(INodeService nodeService)
    {
        _nodeService = nodeService;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RegisterNodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegisterNodeResponse>> RegisterNode(
        [FromBody] RegisterNodeRequest request,
        CancellationToken ct = default)
    {
        var result = await _nodeService.RegisterNodeAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("heartbeat")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HeartbeatResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HeartbeatResponseDto>> Heartbeat(
        [FromBody] NodeHeartbeatRequest request,
        [FromHeader(Name = "X-Node-Id")] string? nodeIdHeader,
        [FromHeader(Name = "X-Node-Signature")] string? signatureHeader,
        [FromHeader(Name = "X-Node-Timestamp")] string? timestampHeader,
        [FromHeader(Name = "X-Node-Secret")] string? secretHeader,
        [FromHeader(Name = "Authorization")] string? authHeader,
        CancellationToken ct = default)
    {
        var result = await _nodeService.ProcessHeartbeatAsync(
            request,
            nodeIdHeader,
            signatureHeader,
            timestampHeader,
            secretHeader,
            authHeader,
            ct);

        return Ok(result);
    }
}
