using Microsoft.AspNetCore.SignalR;
using OmegaCloud.Api.Hubs;
using OmegaCloud.Application.Common.Interfaces;

namespace OmegaCloud.Api.Services;

/// <summary>
/// Dispatches real-time events to connected clients via SignalR Hub (/hubs/omega).
/// </summary>
public class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<OmegaHub, IOmegaHubClient> _hubContext;
    private readonly ILogger<SignalRRealtimeNotifier> _logger;

    public SignalRRealtimeNotifier(
        IHubContext<OmegaHub, IOmegaHubClient> hubContext,
        ILogger<SignalRRealtimeNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyNodeStatusChangedAsync(
        Guid nodeId,
        string status,
        DateTimeOffset lastSeen,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Broadcasting NodeStatusChanged to Admins: NodeId={NodeId}, Status={Status}", nodeId, status);
            await _hubContext.Clients.Group(OmegaHub.AdminsGroup).NodeStatusChanged(nodeId, status, lastSeen);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast NodeStatusChanged for node {NodeId}", nodeId);
        }
    }

    /// <inheritdoc />
    public async Task NotifyFileProgressAsync(
        Guid userId,
        Guid sessionId,
        double percent,
        double speedBytesPerSec,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Sending FileProgress to user {UserId}: SessionId={SessionId}, Percent={Percent}%, Speed={Speed}",
                userId, sessionId, percent, speedBytesPerSec);

            if (userId != Guid.Empty)
            {
                await _hubContext.Clients.User(userId.ToString()).FileProgress(sessionId, percent, speedBytesPerSec);
            }
            else
            {
                await _hubContext.Clients.Group($"session_{sessionId}").FileProgress(sessionId, percent, speedBytesPerSec);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send FileProgress for session {SessionId}", sessionId);
        }
    }

    /// <inheritdoc />
    public async Task NotifyDirectoryChangedAsync(
        Guid userId,
        Guid? folderId,
        string changeType,
        object? item,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Sending DirectoryChanged to user {UserId}: FolderId={FolderId}, ChangeType={ChangeType}",
                userId, folderId, changeType);

            if (userId != Guid.Empty)
            {
                await _hubContext.Clients.User(userId.ToString()).DirectoryChanged(folderId, changeType, item);
            }
            else
            {
                var folderGroup = OmegaHub.GetFolderGroupName(folderId);
                await _hubContext.Clients.Group(folderGroup).DirectoryChanged(folderId, changeType, item);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send DirectoryChanged for user {UserId}", userId);
        }
    }

    /// <inheritdoc />
    public async Task NotifyQuotaUpdatedAsync(
        Guid userId,
        long usedBytes,
        long totalBytes,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Sending QuotaUpdated to user {UserId}: Used={UsedBytes}, Total={TotalBytes}",
                userId, usedBytes, totalBytes);

            await _hubContext.Clients.User(userId.ToString()).QuotaUpdated(usedBytes, totalBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send QuotaUpdated for user {UserId}", userId);
        }
    }

    /// <inheritdoc />
    public async Task NotifyForceLogoutAsync(
        Guid userId,
        string reason,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        try
        {
            if (sessionId.HasValue)
            {
                _logger.LogInformation("Sending ForceLogout to session {SessionId}: Reason={Reason}", sessionId.Value, reason);
                await _hubContext.Clients.Group($"session_{sessionId.Value}").ForceLogout(reason);
            }
            else
            {
                _logger.LogInformation("Sending ForceLogout to user {UserId}: Reason={Reason}", userId, reason);
                await _hubContext.Clients.User(userId.ToString()).ForceLogout(reason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send ForceLogout for user {UserId}, session {SessionId}", userId, sessionId);
        }
    }
}
