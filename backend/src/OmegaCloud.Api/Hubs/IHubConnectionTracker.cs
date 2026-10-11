using Microsoft.AspNetCore.SignalR;

namespace OmegaCloud.Api.Hubs;

public interface IHubConnectionTracker
{
    void TrackConnection(string connectionId, Guid userId, Guid? sessionId, HubCallerContext context);
    void UntrackConnection(string connectionId);
    void AbortSession(Guid sessionId);
    void AbortUser(Guid userId);
}
