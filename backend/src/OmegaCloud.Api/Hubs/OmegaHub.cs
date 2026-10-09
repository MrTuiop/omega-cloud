using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Api.Hubs;

/// <summary>
/// Real-time SignalR Hub for Omega Cloud.
/// Mapped to endpoint /hubs/omega.
/// </summary>
[Authorize]
public class OmegaHub : Hub<IOmegaHubClient>
{
    public const string HubPath = "/hubs/omega";
    public const string AdminsGroup = "Admins";

    private readonly ILogger<OmegaHub> _logger;

    public OmegaHub(ILogger<OmegaHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        var userId = Context.UserIdentifier
            ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        _logger.LogInformation("Client connected to OmegaHub: ConnectionId={ConnectionId}, UserId={UserId}",
            Context.ConnectionId, userId);

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        }

        if (user != null)
        {
            var roleClaim = user.FindFirst(ClaimTypes.Role)?.Value;
            var isAdmin = user.IsInRole("Admin") ||
                          user.IsInRole(UserRole.Admin.ToString()) ||
                          string.Equals(roleClaim, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(roleClaim, "Admin", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(user.FindFirst("role")?.Value, "Admin", StringComparison.OrdinalIgnoreCase);

            if (isAdmin)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
                _logger.LogDebug("Connection {ConnectionId} added to {Group} group", Context.ConnectionId, AdminsGroup);
            }

            var sessionId = user.FindFirst("session_id")?.Value;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var normalizedSessionId = Guid.TryParse(sessionId, out var parsedSessionId)
                    ? parsedSessionId.ToString()
                    : sessionId;

                await Groups.AddToGroupAsync(Context.ConnectionId, $"session_{normalizedSessionId}");
                _logger.LogDebug("Connection {ConnectionId} added to session group session_{SessionId}", Context.ConnectionId, normalizedSessionId);
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(exception, "Client disconnected from OmegaHub: ConnectionId={ConnectionId}, UserId={UserId}",
            Context.ConnectionId, Context.UserIdentifier);

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribes the current connection to real-time events for a specific folder.
    /// </summary>
    public async Task SubscribeToFolder(Guid? folderId)
    {
        var groupName = GetFolderGroupName(folderId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogDebug("Connection {ConnectionId} subscribed to {Group}", Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Unsubscribes the current connection from real-time events for a specific folder.
    /// </summary>
    public async Task UnsubscribeFromFolder(Guid? folderId)
    {
        var groupName = GetFolderGroupName(folderId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogDebug("Connection {ConnectionId} unsubscribed from {Group}", Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Subscribes the current connection to progress events for a specific upload session.
    /// </summary>
    public async Task SubscribeToUploadSession(Guid sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"session_{sessionId}");
        _logger.LogDebug("Connection {ConnectionId} subscribed to upload session {SessionId}", Context.ConnectionId, sessionId);
    }

    /// <summary>
    /// Unsubscribes the current connection from progress events for a specific upload session.
    /// </summary>
    public async Task UnsubscribeFromUploadSession(Guid sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session_{sessionId}");
        _logger.LogDebug("Connection {ConnectionId} unsubscribed from upload session {SessionId}", Context.ConnectionId, sessionId);
    }

    /// <summary>
    /// Simple ping method to check connectivity and round-trip latency.
    /// </summary>
    public Task<string> Ping()
    {
        return Task.FromResult("pong");
    }

    public static string GetFolderGroupName(Guid? folderId) =>
        folderId.HasValue ? $"folder_{folderId.Value}" : "folder_root";
}
