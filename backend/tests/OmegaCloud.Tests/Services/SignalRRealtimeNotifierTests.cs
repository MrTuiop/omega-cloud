using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using OmegaCloud.Api.Hubs;
using OmegaCloud.Api.Services;
using Xunit;

namespace OmegaCloud.Tests.Services;

public class SignalRRealtimeNotifierTests
{
    private class TestHubClients : IHubClients<IOmegaHubClient>
    {
        public TestOmegaHubClient AllClient { get; } = new();
        public Dictionary<string, TestOmegaHubClient> GroupsMap { get; } = new();
        public Dictionary<string, TestOmegaHubClient> UsersMap { get; } = new();

        public IOmegaHubClient All => AllClient;

        public IOmegaHubClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => AllClient;

        public IOmegaHubClient Client(string connectionId) => AllClient;

        public IOmegaHubClient Clients(IReadOnlyList<string> connectionIds) => AllClient;

        public IOmegaHubClient Group(string groupName)
        {
            if (!GroupsMap.TryGetValue(groupName, out var client))
            {
                client = new TestOmegaHubClient();
                GroupsMap[groupName] = client;
            }
            return client;
        }

        public IOmegaHubClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Group(groupName);

        public IOmegaHubClient Groups(IReadOnlyList<string> groupNames) => AllClient;

        public IOmegaHubClient User(string userId)
        {
            if (!UsersMap.TryGetValue(userId, out var client))
            {
                client = new TestOmegaHubClient();
                UsersMap[userId] = client;
            }
            return client;
        }

        public IOmegaHubClient Users(IReadOnlyList<string> userIds) => AllClient;
    }

    private class TestOmegaHubClient : IOmegaHubClient
    {
        public List<(Guid nodeId, string status, DateTimeOffset lastSeen)> NodeStatusEvents { get; } = new();
        public List<(Guid sessionId, double percent, double speed)> FileProgressEvents { get; } = new();
        public List<(Guid? folderId, string changeType, object? item)> DirectoryChangedEvents { get; } = new();
        public List<(long usedBytes, long totalBytes)> QuotaUpdatedEvents { get; } = new();
        public List<string> ForceLogoutEvents { get; } = new();

        public Task NodeStatusChanged(Guid nodeId, string status, DateTimeOffset lastSeen)
        {
            NodeStatusEvents.Add((nodeId, status, lastSeen));
            return Task.CompletedTask;
        }

        public Task FileProgress(Guid sessionId, double percent, double speedBytesPerSec)
        {
            FileProgressEvents.Add((sessionId, percent, speedBytesPerSec));
            return Task.CompletedTask;
        }

        public Task DirectoryChanged(Guid? folderId, string changeType, object? item)
        {
            DirectoryChangedEvents.Add((folderId, changeType, item));
            return Task.CompletedTask;
        }

        public Task QuotaUpdated(long usedBytes, long totalBytes)
        {
            QuotaUpdatedEvents.Add((usedBytes, totalBytes));
            return Task.CompletedTask;
        }

        public Task ForceLogout(string reason)
        {
            ForceLogoutEvents.Add(reason);
            return Task.CompletedTask;
        }
    }

    private class TestHubContext : IHubContext<OmegaHub, IOmegaHubClient>
    {
        public TestHubClients HubClients { get; } = new();

        public IHubClients<IOmegaHubClient> Clients => HubClients;

        public IGroupManager Groups => throw new NotImplementedException();
    }

    [Fact]
    public async Task NotifyNodeStatusChangedAsync_DispatchesToAdminsGroup()
    {
        // Arrange
        var context = new TestHubContext();
        var notifier = new SignalRRealtimeNotifier(context, NullLogger<SignalRRealtimeNotifier>.Instance);
        var nodeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Act
        await notifier.NotifyNodeStatusChangedAsync(nodeId, "Degraded", now);

        // Assert
        Assert.True(context.HubClients.GroupsMap.ContainsKey(OmegaHub.AdminsGroup));
        var client = context.HubClients.GroupsMap[OmegaHub.AdminsGroup];
        Assert.Single(client.NodeStatusEvents);
        Assert.Equal(nodeId, client.NodeStatusEvents[0].nodeId);
        Assert.Equal("Degraded", client.NodeStatusEvents[0].status);
        Assert.Equal(now, client.NodeStatusEvents[0].lastSeen);
    }

    [Fact]
    public async Task NotifyFileProgressAsync_WhenUserIdProvided_DispatchesToUser()
    {
        // Arrange
        var context = new TestHubContext();
        var notifier = new SignalRRealtimeNotifier(context, NullLogger<SignalRRealtimeNotifier>.Instance);
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Act
        await notifier.NotifyFileProgressAsync(userId, sessionId, 45.5, 1024.0);

        // Assert
        Assert.True(context.HubClients.UsersMap.ContainsKey(userId.ToString()));
        var client = context.HubClients.UsersMap[userId.ToString()];
        Assert.Single(client.FileProgressEvents);
        Assert.Equal(sessionId, client.FileProgressEvents[0].sessionId);
        Assert.Equal(45.5, client.FileProgressEvents[0].percent);
        Assert.Equal(1024.0, client.FileProgressEvents[0].speed);
    }

    [Fact]
    public async Task NotifyFileProgressAsync_WhenUserIdEmpty_DispatchesToSessionGroup()
    {
        // Arrange
        var context = new TestHubContext();
        var notifier = new SignalRRealtimeNotifier(context, NullLogger<SignalRRealtimeNotifier>.Instance);
        var sessionId = Guid.NewGuid();

        // Act
        await notifier.NotifyFileProgressAsync(Guid.Empty, sessionId, 90.0, 5000.0);

        // Assert
        var groupKey = $"session_{sessionId}";
        Assert.True(context.HubClients.GroupsMap.ContainsKey(groupKey));
        var client = context.HubClients.GroupsMap[groupKey];
        Assert.Single(client.FileProgressEvents);
        Assert.Equal(sessionId, client.FileProgressEvents[0].sessionId);
    }

    [Fact]
    public async Task NotifyDirectoryChangedAsync_WhenUserIdProvided_DispatchesToUser()
    {
        // Arrange
        var context = new TestHubContext();
        var notifier = new SignalRRealtimeNotifier(context, NullLogger<SignalRRealtimeNotifier>.Instance);
        var userId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var itemData = new { Name = "test.txt" };

        // Act
        await notifier.NotifyDirectoryChangedAsync(userId, folderId, "Created", itemData);

        // Assert
        Assert.True(context.HubClients.UsersMap.ContainsKey(userId.ToString()));
        var client = context.HubClients.UsersMap[userId.ToString()];
        Assert.Single(client.DirectoryChangedEvents);
        Assert.Equal(folderId, client.DirectoryChangedEvents[0].folderId);
        Assert.Equal("Created", client.DirectoryChangedEvents[0].changeType);
        Assert.Equal(itemData, client.DirectoryChangedEvents[0].item);
    }

    [Fact]
    public async Task NotifyQuotaUpdatedAsync_DispatchesToUser()
    {
        // Arrange
        var context = new TestHubContext();
        var notifier = new SignalRRealtimeNotifier(context, NullLogger<SignalRRealtimeNotifier>.Instance);
        var userId = Guid.NewGuid();

        // Act
        await notifier.NotifyQuotaUpdatedAsync(userId, 5000L, 100000L);

        // Assert
        Assert.True(context.HubClients.UsersMap.ContainsKey(userId.ToString()));
        var client = context.HubClients.UsersMap[userId.ToString()];
        Assert.Single(client.QuotaUpdatedEvents);
        Assert.Equal(5000L, client.QuotaUpdatedEvents[0].usedBytes);
        Assert.Equal(100000L, client.QuotaUpdatedEvents[0].totalBytes);
    }

    [Fact]
    public async Task NotifyForceLogoutAsync_WhenSessionIdProvided_DispatchesToSessionGroup()
    {
        // Arrange
        var context = new TestHubContext();
        var notifier = new SignalRRealtimeNotifier(context, NullLogger<SignalRRealtimeNotifier>.Instance);
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Act
        await notifier.NotifyForceLogoutAsync(userId, "Session expired", sessionId);

        // Assert
        var sessionGroup = $"session_{sessionId}";
        Assert.True(context.HubClients.GroupsMap.ContainsKey(sessionGroup));
        var client = context.HubClients.GroupsMap[sessionGroup];
        Assert.Single(client.ForceLogoutEvents);
        Assert.Equal("Session expired", client.ForceLogoutEvents[0]);
    }

    [Fact]
    public async Task NotifyForceLogoutAsync_WhenSessionIdNull_DispatchesToUser()
    {
        // Arrange
        var context = new TestHubContext();
        var notifier = new SignalRRealtimeNotifier(context, NullLogger<SignalRRealtimeNotifier>.Instance);
        var userId = Guid.NewGuid();

        // Act
        await notifier.NotifyForceLogoutAsync(userId, "User deactivated", null);

        // Assert
        Assert.True(context.HubClients.UsersMap.ContainsKey(userId.ToString()));
        var client = context.HubClients.UsersMap[userId.ToString()];
        Assert.Single(client.ForceLogoutEvents);
        Assert.Equal("User deactivated", client.ForceLogoutEvents[0]);
    }
}
