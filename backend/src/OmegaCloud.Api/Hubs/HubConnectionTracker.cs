using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace OmegaCloud.Api.Hubs;

public class HubConnectionTracker : IHubConnectionTracker
{
    private readonly ConcurrentDictionary<string, (Guid UserId, Guid? SessionId, HubCallerContext Context)> _connections = new();
    private readonly ILogger<HubConnectionTracker> _logger;

    public HubConnectionTracker(ILogger<HubConnectionTracker> logger)
    {
        _logger = logger;
    }

    public void TrackConnection(string connectionId, Guid userId, Guid? sessionId, HubCallerContext context)
    {
        _connections[connectionId] = (userId, sessionId, context);
        _logger.LogDebug("Tracking SignalR connection {ConnectionId} (UserId={UserId}, SessionId={SessionId})",
            connectionId, userId, sessionId);
    }

    public void UntrackConnection(string connectionId)
    {
        _connections.TryRemove(connectionId, out _);
    }

    public void AbortSession(Guid sessionId)
    {
        foreach (var (connId, entry) in _connections)
        {
            if (entry.SessionId == sessionId)
            {
                _connections.TryRemove(connId, out _);
                try
                {
                    _logger.LogInformation("Aborting SignalR connection {ConnectionId} for revoked session {SessionId}",
                        connId, sessionId);
                    entry.Context.Abort();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error aborting connection {ConnectionId}", connId);
                }
            }
        }
    }

    public void AbortUser(Guid userId)
    {
        foreach (var (connId, entry) in _connections)
        {
            if (entry.UserId == userId)
            {
                _connections.TryRemove(connId, out _);
                try
                {
                    _logger.LogInformation("Aborting SignalR connection {ConnectionId} for revoked user {UserId}",
                        connId, userId);
                    entry.Context.Abort();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error aborting connection {ConnectionId}", connId);
                }
            }
        }
    }
}
