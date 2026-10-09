namespace OmegaCloud.Api.Hubs;

/// <summary>
/// Strongly-typed client contract for Omega Cloud SignalR hub (/hubs/omega).
/// Matches Section 3.5 of the Omega Cloud Master Plan.
/// </summary>
public interface IOmegaHubClient
{
    /// <summary>
    /// Broadcast to administrators when a node goes offline, degraded, or recovers online.
    /// </summary>
    Task NodeStatusChanged(Guid nodeId, string status, DateTimeOffset lastSeen);

    /// <summary>
    /// Progress notification during file upload, chunk assembly, or transfer.
    /// </summary>
    Task FileProgress(Guid sessionId, double percent, double speedBytesPerSec);

    /// <summary>
    /// File tree update triggered by file/folder creation, modification, deletion, restore, or upload.
    /// </summary>
    Task DirectoryChanged(Guid? folderId, string changeType, object? item);

    /// <summary>
    /// Notification of updated storage quota metrics.
    /// </summary>
    Task QuotaUpdated(long usedBytes, long totalBytes);

    /// <summary>
    /// Force logout notification sent when a user session is revoked or terminated.
    /// </summary>
    Task ForceLogout(string reason);
}
