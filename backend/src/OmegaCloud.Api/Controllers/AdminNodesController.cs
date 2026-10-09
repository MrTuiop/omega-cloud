using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Nodes;
using OmegaCloud.Application.Services;

namespace OmegaCloud.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/v1/admin/nodes")]
public class AdminNodesController : ControllerBase
{
    private readonly INodeService _nodeService;
    private readonly IAuditService _auditService;
    private readonly ICurrentUser _currentUser;

    public AdminNodesController(
        INodeService nodeService,
        IAuditService auditService,
        ICurrentUser currentUser)
    {
        _nodeService = nodeService;
        _auditService = auditService;
        _currentUser = currentUser;
    }

    [HttpGet]
    [HttpGet("/api/v1/nodes")]
    [ProducesResponseType(typeof(IReadOnlyList<StorageNodeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<StorageNodeDto>>> GetNodes(CancellationToken ct = default)
    {
        var nodes = await _nodeService.GetAllNodesAsync(ct);
        return Ok(nodes);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(StorageNodeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StorageNodeDto>> GetNode(Guid id, CancellationToken ct = default)
    {
        var node = await _nodeService.GetNodeByIdAsync(id, ct);
        return Ok(node);
    }

    [HttpPost("generate-token")]
    [HttpPost("/api/v1/nodes/generate-token")]
    [ProducesResponseType(typeof(NodeRegistrationTokenDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<NodeRegistrationTokenDto>> GenerateToken(
        [FromBody] GenerateNodeTokenRequest? request,
        CancellationToken ct = default)
    {
        var token = await _nodeService.GenerateRegistrationTokenAsync(request ?? new GenerateNodeTokenRequest(), ct);

        await _auditService.LogAsync(
            action: "NodeTokenGenerated",
            resource: "StorageNodes",
            resourceId: token.RegistrationKey,
            userId: _currentUser.UserId,
            username: _currentUser.Username,
            ipAddress: GetClientIpAddress(),
            details: new { nodeName = token.Name, nodeId = token.NodeId },
            ct: ct);

        return Ok(token);
    }

    [HttpDelete("{id:guid}")]
    [HttpDelete("/api/v1/nodes/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteNode(Guid id, CancellationToken ct = default)
    {
        await _nodeService.DeleteOrArchiveNodeAsync(id, ct);

        await _auditService.LogAsync(
            action: "NodeDeleted",
            resource: "StorageNodes",
            resourceId: id.ToString(),
            userId: _currentUser.UserId,
            username: _currentUser.Username,
            ipAddress: GetClientIpAddress(),
            details: new { nodeId = id },
            ct: ct);

        return NoContent();
    }

    [HttpPatch("{id:guid}")]
    [HttpPatch("/api/v1/nodes/{id:guid}")]
    [ProducesResponseType(typeof(StorageNodeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StorageNodeDto>> UpdateNode(
        Guid id,
        [FromBody] UpdateStorageNodeRequest request,
        CancellationToken ct = default)
    {
        var node = await _nodeService.UpdateNodeAsync(id, request, ct);

        await _auditService.LogAsync(
            action: "NodeUpdated",
            resource: "StorageNodes",
            resourceId: id.ToString(),
            userId: _currentUser.UserId,
            username: _currentUser.Username,
            ipAddress: GetClientIpAddress(),
            details: new { nodeName = node.Name, status = node.Status },
            ct: ct);

        return Ok(node);
    }

    [HttpPost("{id:guid}/ping")]
    [ProducesResponseType(typeof(NodePingResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NodePingResultDto>> PingNode(Guid id, CancellationToken ct = default)
    {
        var result = await _nodeService.PingNodeAsync(id, ct);

        await _auditService.LogAsync(
            action: "NodePinged",
            resource: "StorageNodes",
            resourceId: id.ToString(),
            userId: _currentUser.UserId,
            username: _currentUser.Username,
            ipAddress: GetClientIpAddress(),
            details: new { isReachable = result.IsReachable, latencyMs = result.LatencyMs, status = result.Status },
            ct: ct);

        return Ok(result);
    }

    private string GetClientIpAddress()
    {
        if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            var ip = forwarded.ToString().Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(ip))
            {
                return ip;
            }
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
