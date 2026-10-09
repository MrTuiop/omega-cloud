using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using OmegaCloud.Api.Hubs;
using OmegaCloud.Domain.Enums;
using Xunit;

namespace OmegaCloud.Tests.Hubs;

public class OmegaHubUnitTests
{
    private class TestHubCallerContext : HubCallerContext
    {
        public override string ConnectionId { get; }
        public override string? UserIdentifier { get; }
        public override ClaimsPrincipal? User { get; }
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted { get; } = CancellationToken.None;

        public TestHubCallerContext(string connectionId, string? userId, ClaimsPrincipal? user)
        {
            ConnectionId = connectionId;
            UserIdentifier = userId;
            User = user;
        }

        public override void Abort() { }
    }

    private class TestGroupManager : IGroupManager
    {
        public List<(string connectionId, string groupName)> Added { get; } = new();
        public List<(string connectionId, string groupName)> Removed { get; } = new();

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Removed.Add((connectionId, groupName));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task OnConnectedAsync_WhenUserIsAdmin_AddsToAdminsUserAndSessionGroups()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, UserRole.Admin.ToString()),
            new("session_id", sessionId.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = new TestHubCallerContext("conn_123", userId.ToString(), principal);
        var groupManager = new TestGroupManager();

        var hub = new OmegaHub(NullLogger<OmegaHub>.Instance)
        {
            Context = context,
            Groups = groupManager
        };

        // Act
        await hub.OnConnectedAsync();

        // Assert
        Assert.Contains(("conn_123", $"user_{userId}"), groupManager.Added);
        Assert.Contains(("conn_123", OmegaHub.AdminsGroup), groupManager.Added);
        Assert.Contains(("conn_123", $"session_{sessionId}"), groupManager.Added);
    }

    [Fact]
    public async Task OnConnectedAsync_WhenUserIsRegularUser_DoesNotAddToAdminsGroup()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, UserRole.User.ToString()),
            new("session_id", sessionId.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = new TestHubCallerContext("conn_456", userId.ToString(), principal);
        var groupManager = new TestGroupManager();

        var hub = new OmegaHub(NullLogger<OmegaHub>.Instance)
        {
            Context = context,
            Groups = groupManager
        };

        // Act
        await hub.OnConnectedAsync();

        // Assert
        Assert.Contains(("conn_456", $"user_{userId}"), groupManager.Added);
        Assert.Contains(("conn_456", $"session_{sessionId}"), groupManager.Added);
        Assert.DoesNotContain(("conn_456", OmegaHub.AdminsGroup), groupManager.Added);
    }

    [Fact]
    public async Task SubscribeToFolder_WithValidFolderId_AddsToFolderGroup()
    {
        // Arrange
        var folderId = Guid.NewGuid();
        var context = new TestHubCallerContext("conn_sub", null, null);
        var groupManager = new TestGroupManager();

        var hub = new OmegaHub(NullLogger<OmegaHub>.Instance)
        {
            Context = context,
            Groups = groupManager
        };

        // Act
        await hub.SubscribeToFolder(folderId);

        // Assert
        Assert.Contains(("conn_sub", $"folder_{folderId}"), groupManager.Added);
    }

    [Fact]
    public async Task SubscribeToFolder_WithNullFolderId_AddsToRootFolderGroup()
    {
        // Arrange
        var context = new TestHubCallerContext("conn_sub_root", null, null);
        var groupManager = new TestGroupManager();

        var hub = new OmegaHub(NullLogger<OmegaHub>.Instance)
        {
            Context = context,
            Groups = groupManager
        };

        // Act
        await hub.SubscribeToFolder(null);

        // Assert
        Assert.Contains(("conn_sub_root", "folder_root"), groupManager.Added);
    }

    [Fact]
    public async Task UnsubscribeFromFolder_RemovesFromFolderGroup()
    {
        // Arrange
        var folderId = Guid.NewGuid();
        var context = new TestHubCallerContext("conn_unsub", null, null);
        var groupManager = new TestGroupManager();

        var hub = new OmegaHub(NullLogger<OmegaHub>.Instance)
        {
            Context = context,
            Groups = groupManager
        };

        // Act
        await hub.UnsubscribeFromFolder(folderId);

        // Assert
        Assert.Contains(("conn_unsub", $"folder_{folderId}"), groupManager.Removed);
    }

    [Fact]
    public async Task Ping_ReturnsPong()
    {
        // Arrange
        var hub = new OmegaHub(NullLogger<OmegaHub>.Instance);

        // Act
        var result = await hub.Ping();

        // Assert
        Assert.Equal("pong", result);
    }

    [Fact]
    public async Task SubscribeToUploadSession_AddsToSessionGroup()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var context = new TestHubCallerContext("conn_upload", null, null);
        var groupManager = new TestGroupManager();

        var hub = new OmegaHub(NullLogger<OmegaHub>.Instance)
        {
            Context = context,
            Groups = groupManager
        };

        // Act
        await hub.SubscribeToUploadSession(sessionId);

        // Assert
        Assert.Contains(("conn_upload", $"session_{sessionId}"), groupManager.Added);
    }

    [Fact]
    public async Task UnsubscribeFromUploadSession_RemovesFromSessionGroup()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var context = new TestHubCallerContext("conn_unupload", null, null);
        var groupManager = new TestGroupManager();

        var hub = new OmegaHub(NullLogger<OmegaHub>.Instance)
        {
            Context = context,
            Groups = groupManager
        };

        // Act
        await hub.UnsubscribeFromUploadSession(sessionId);

        // Assert
        Assert.Contains(("conn_unupload", $"session_{sessionId}"), groupManager.Removed);
    }

    [Fact]
    public async Task OnConnectedAsync_WhenUserIdentifierNull_FallbacksToSubClaim()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var claims = new List<Claim>
        {
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.Role, "Admin")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = new TestHubCallerContext("conn_sub_claim", null, principal);
        var groupManager = new TestGroupManager();

        var hub = new OmegaHub(NullLogger<OmegaHub>.Instance)
        {
            Context = context,
            Groups = groupManager
        };

        // Act
        await hub.OnConnectedAsync();

        // Assert
        Assert.Contains(("conn_sub_claim", $"user_{userId}"), groupManager.Added);
        Assert.Contains(("conn_sub_claim", OmegaHub.AdminsGroup), groupManager.Added);
    }

    [Fact]
    public async Task OnConnectedAsync_NormalizesSessionIdGuid()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var rawGuid = Guid.NewGuid();
        var upperSessionId = rawGuid.ToString("D").ToUpperInvariant();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("session_id", upperSessionId)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = new TestHubCallerContext("conn_norm", userId.ToString(), principal);
        var groupManager = new TestGroupManager();

        var hub = new OmegaHub(NullLogger<OmegaHub>.Instance)
        {
            Context = context,
            Groups = groupManager
        };

        // Act
        await hub.OnConnectedAsync();

        // Assert
        Assert.Contains(("conn_norm", $"session_{rawGuid}"), groupManager.Added);
    }
}
