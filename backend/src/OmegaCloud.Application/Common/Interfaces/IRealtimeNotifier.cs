namespace OmegaCloud.Application.Common.Interfaces;

/// <summary>
/// Abstraction for publishing real-time events to connected clients via SignalR or message brokers.
/// </summary>
public interface IRealtimeNotifier
{
    /// <summary>
    /// Notifies administrators about a storage node's status change or recovery.
    /// </summary>
    Task NotifyNodeStatusChangedAsync(Guid nodeId, string status, DateTimeOffset lastSeen, CancellationToken ct = default);

    /// <summary>
    /// Notifies the owning user about file assembly, upload, or transfer progress.
    /// </summary>
    Task NotifyFileProgressAsync(Guid userId, Guid sessionId, double percent, double speedBytesPerSec, CancellationToken ct = default);

    /// <summary>
    /// Notifies the owning user (and folder watchers) about changes to the directory tree.
    /// </summary>
    Task NotifyDirectoryChangedAsync(Guid userId, Guid? folderId, string changeType, object? item, CancellationToken ct = default);

    /// <summary>
    /// Notifies the owning user about changes to their used/allocated storage quota.
    /// </summary>
    Task NotifyQuotaUpdatedAsync(Guid userId, long usedBytes, long totalBytes, CancellationToken ct = default);

    /// <summary>
    /// Forcefully logs out a user or a specific device session (e.g. upon session revocation).
    /// </summary>
    Task NotifyForceLogoutAsync(Guid userId, string reason, Guid? sessionId = null, CancellationToken ct = default);
}

/// <summary>
/// Null-object implementation of <see cref="IRealtimeNotifier"/> for test isolation and fallback.
/// </summary>
public class NullRealtimeNotifier : IRealtimeNotifier
{
    public static readonly NullRealtimeNotifier Instance = new();

    public Task NotifyNodeStatusChangedAsync(Guid nodeId, string status, DateTimeOffset lastSeen, CancellationToken ct = default) => Task.CompletedTask;

    public Task NotifyFileProgressAsync(Guid userId, Guid sessionId, double percent, double speedBytesPerSec, CancellationToken ct = default) => Task.CompletedTask;

    public Task NotifyDirectoryChangedAsync(Guid userId, Guid? folderId, string changeType, object? item, CancellationToken ct = default) => Task.CompletedTask;

    public Task NotifyQuotaUpdatedAsync(Guid userId, long usedBytes, long totalBytes, CancellationToken ct = default) => Task.CompletedTask;

    public Task NotifyForceLogoutAsync(Guid userId, string reason, Guid? sessionId = null, CancellationToken ct = default) => Task.CompletedTask;
}
