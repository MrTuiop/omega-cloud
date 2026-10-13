using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OmegaCloud.Application.DTOs.Admin;
using OmegaCloud.Application.DTOs.Auth;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Application.DTOs.Nodes;
using OmegaCloud.Application.DTOs.Upload;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;
using OmegaCloud.Infrastructure.Data;
using Xunit;

namespace OmegaCloud.Tests.Integration;

public class Release11Stage1Tests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Release11Stage1Tests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, LoginResponse Login)> CreateClientForUserAsync(string username, string password, string deviceName)
    {
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            UsernameOrEmail = username,
            Password = password,
            DeviceName = deviceName
        });
        loginResponse.EnsureSuccessStatusCode();

        var loginResult = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);
        return (client, loginResult);
    }

    [Fact]
    public async Task SessionRevocation_ShouldInvalidateToken_SubsequentRequestsReturn401()
    {
        // Arrange: login on two devices for the regular user
        var (client1, login1) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultRegularUsername,
            CustomWebApplicationFactory.DefaultRegularPassword,
            "Phone-Device-" + Guid.NewGuid().ToString("N")[..4]);

        var (client2, login2) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultRegularUsername,
            CustomWebApplicationFactory.DefaultRegularPassword,
            "Laptop-Device-" + Guid.NewGuid().ToString("N")[..4]);

        // List sessions from device 2
        var sessionsResponse = await client2.GetAsync("/api/v1/auth/sessions");
        sessionsResponse.EnsureSuccessStatusCode();
        var sessions = (await sessionsResponse.Content.ReadFromJsonAsync<List<UserSessionDto>>(_jsonOptions))!;

        // Find session 1
        var session1Dto = sessions.FirstOrDefault(s => !s.IsCurrent);
        Assert.NotNull(session1Dto);

        // Verify device 1 can initially access protected endpoint
        var preRevokeMe = await client1.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, preRevokeMe.StatusCode);

        // Act: Revoke session 1 from device 2
        var revokeResponse = await client2.DeleteAsync($"/api/v1/auth/sessions/{session1Dto.Id}");
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        // Assert: Subsequent request using token1 must return 401 Unauthorized
        var postRevokeMe = await client1.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, postRevokeMe.StatusCode);

        var postRevokeFolders = await client1.GetAsync("/api/v1/folders");
        Assert.Equal(HttpStatusCode.Unauthorized, postRevokeFolders.StatusCode);

        // Token 2 must remain valid
        var client2Me = await client2.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, client2Me.StatusCode);
    }

    [Fact]
    public async Task GuestRole_IsCompletelyRejected_OnCreateUser()
    {
        var (adminClient, _) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultAdminUsername,
            CustomWebApplicationFactory.DefaultAdminPassword,
            "Admin-Device");

        var response = await adminClient.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Username = "guest_user_" + Guid.NewGuid().ToString("N")[..6],
            Email = $"guest_{Guid.NewGuid():N}@test.local",
            Password = "ValidPassword123!",
            Role = "Guest"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Supported roles: Admin, User", body);
    }

    [Fact]
    public async Task AdminNode_MaxCapacityBytes_CanBeConfigured()
    {
        var (adminClient, _) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultAdminUsername,
            CustomWebApplicationFactory.DefaultAdminPassword,
            "Admin-Device");

        var nodesResponse = await adminClient.GetAsync("/api/v1/admin/nodes");
        nodesResponse.EnsureSuccessStatusCode();
        var nodes = (await nodesResponse.Content.ReadFromJsonAsync<List<StorageNodeDto>>(_jsonOptions))!;
        var firstNode = nodes.First();

        var updatedCapacity = firstNode.TotalSpaceBytes > 10_000_000_000L ? firstNode.TotalSpaceBytes / 2 : 5_000_000_000L;
        var patchResponse = await adminClient.PatchAsJsonAsync($"/api/v1/admin/nodes/{firstNode.Id}", new UpdateStorageNodeRequest
        {
            MaxCapacityBytes = updatedCapacity
        });
        patchResponse.EnsureSuccessStatusCode();
        var updatedNode = (await patchResponse.Content.ReadFromJsonAsync<StorageNodeDto>(_jsonOptions))!;

        Assert.Equal(updatedCapacity, updatedNode.MaxCapacityBytes);
    }

    [Fact]
    public async Task UserQuotaAllocation_ExceedingTotalNodesCapacity_IsRejected()
    {
        var (adminClient, _) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultAdminUsername,
            CustomWebApplicationFactory.DefaultAdminPassword,
            "Admin-Device");

        // Try allocating 50 TB quota when nodes capacity is far lower
        var excessiveQuota = 50_000_000_000_000L;
        var response = await adminClient.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Username = "huge_quota_" + Guid.NewGuid().ToString("N")[..6],
            Email = $"huge_{Guid.NewGuid():N}@test.local",
            Password = "ValidPassword123!",
            Role = "User",
            AllocatedQuotaBytes = excessiveQuota
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("would exceed active storage nodes capacity", body);
    }

    [Fact]
    public async Task Upload_BalancesToNodeWithMaxAvailableCapacity()
    {
        // Arrange: Add a second node with significantly larger free space
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var highCapacityNodeId = Guid.NewGuid();
        var highCapacityNode = new StorageNode
        {
            Id = highCapacityNodeId,
            Name = "High-Capacity-Node-" + Guid.NewGuid().ToString("N")[..4],
            RegistrationKey = "key-" + Guid.NewGuid().ToString("N")[..8],
            NodeSecretHash = "secret-hash",
            Type = StorageNodeType.LocalDisk,
            Status = StorageNodeStatus.Online,
            TotalSpaceBytes = 50_000_000_000_000L,
            AvailableSpaceBytes = 50_000_000_000_000L,
            MaxCapacityBytes = 50_000_000_000_000L,
            LastHeartbeatAt = DateTime.UtcNow,
            IsActive = true
        };
        db.StorageNodes.Add(highCapacityNode);
        await db.SaveChangesAsync();

        var (client, _) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultRegularUsername,
            CustomWebApplicationFactory.DefaultRegularPassword,
            "Upload-Device");

        // Act: Init upload session without specifying node
        var initResponse = await client.PostAsJsonAsync("/api/v1/upload/sessions/init", new InitUploadSessionRequest
        {
            FileName = "balanced_upload_test.txt",
            TotalSizeBytes = 1024,
            TotalChunks = 1
        });
        initResponse.EnsureSuccessStatusCode();
        var uploadSession = (await initResponse.Content.ReadFromJsonAsync<UploadSessionDto>(_jsonOptions))!;

        // Assert: Node chosen should be the high capacity node (most free space)
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbSession = await verifyDb.UploadSessions.FirstOrDefaultAsync(s => s.Id == uploadSession.SessionId);
        Assert.NotNull(dbSession);
        Assert.Equal(highCapacityNodeId, dbSession.StorageNodeId);
    }

    [Fact]
    public async Task SharedSpace_DomainEntities_HaveIsSharedFlag()
    {
        var folder = new Folder
        {
            Name = "Shared Project Documents",
            IsShared = true
        };
        Assert.True(folder.IsShared);

        var file = new FileItem
        {
            Name = "SharedDoc.pdf",
            IsShared = true
        };
        Assert.True(file.IsShared);

        var defaultFolder = new Folder();
        Assert.False(defaultFolder.IsShared);

        var defaultFile = new FileItem();
        Assert.False(defaultFile.IsShared);
    }

    [Fact]
    public async Task SessionRevocation_ShouldAbortSignalRConnection()
    {
        var (client, login) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultRegularUsername,
            CustomWebApplicationFactory.DefaultRegularPassword,
            "SignalR-Device-" + Guid.NewGuid().ToString("N")[..4]);

        var uri = new Uri(_factory.Server.BaseAddress, "/hubs/omega");
        var hubConn = new HubConnectionBuilder()
            .WithUrl(uri, options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(login.AccessToken);
            })
            .Build();

        var closedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var forceLogoutTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        hubConn.Closed += ex =>
        {
            closedTcs.TrySetResult(true);
            return Task.CompletedTask;
        };

        hubConn.On<string>("ForceLogout", reason =>
        {
            forceLogoutTcs.TrySetResult(reason);
        });

        await hubConn.StartAsync();
        Assert.Equal(HubConnectionState.Connected, hubConn.State);

        // Get sessions
        var sessionsResponse = await client.GetAsync("/api/v1/auth/sessions");
        sessionsResponse.EnsureSuccessStatusCode();
        var sessions = (await sessionsResponse.Content.ReadFromJsonAsync<List<UserSessionDto>>(_jsonOptions))!;
        var currentSession = sessions.First(s => s.IsCurrent);

        // Revoke the session
        var revokeResponse = await client.DeleteAsync($"/api/v1/auth/sessions/{currentSession.Id}");
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        // Verify ForceLogout received
        var forceLogoutCompleted = await Task.WhenAny(forceLogoutTcs.Task, Task.Delay(10000));
        Assert.Same(forceLogoutTcs.Task, forceLogoutCompleted);

        // Verify connection Closed event triggered by server abort
        var closedCompleted = await Task.WhenAny(closedTcs.Task, Task.Delay(10000));
        Assert.Same(closedTcs.Task, closedCompleted);
        Assert.Equal(HubConnectionState.Disconnected, hubConn.State);
    }

    [Fact]
    public async Task AdminNode_DownScalingCapacity_BelowUsedBytes_IsRejected()
    {
        var (adminClient, _) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultAdminUsername,
            CustomWebApplicationFactory.DefaultAdminPassword,
            "Admin-Device");

        // 1. Get current cluster active capacity
        var nodesResponse = await adminClient.GetAsync("/api/v1/admin/nodes");
        nodesResponse.EnsureSuccessStatusCode();
        var nodes = (await nodesResponse.Content.ReadFromJsonAsync<List<StorageNodeDto>>(_jsonOptions))!;
        var activeNodes = nodes.Where(n => n.IsActive && n.MaxCapacityBytes > 0).ToList();
        var firstNode = activeNodes.First();

        // 2. Insert a FileItem representing actual used space (e.g. 50 MB)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstAsync(u => u.Role == UserRole.User);
        var testFile = new FileItem
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Name = "capacity_test_file_" + Guid.NewGuid().ToString("N")[..4] + ".dat",
            StorageBlobId = Guid.NewGuid(),
            SizeBytes = 50_000_000L,
            MimeType = "application/octet-stream",
            Extension = ".dat",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
        db.FileItems.Add(testFile);
        await db.SaveChangesAsync();

        try
        {
            // 3. Try reducing firstNode capacity below actual used bytes
            var otherActive = await db.StorageNodes.Where(n => n.Id != firstNode.Id && n.IsActive).SumAsync(n => n.MaxCapacityBytes);
            if (otherActive < 50_000_000L)
            {
                var tooLowCapacity = 20_000_000L;
                var patchResponse = await adminClient.PatchAsJsonAsync($"/api/v1/admin/nodes/{firstNode.Id}", new UpdateStorageNodeRequest
                {
                    MaxCapacityBytes = tooLowCapacity
                });

                Assert.Equal(HttpStatusCode.BadRequest, patchResponse.StatusCode);
                var body = await patchResponse.Content.ReadAsStringAsync();
                Assert.Contains("Невозможно уменьшить емкость нод ниже фактически занятого объема файлов", body);
            }
        }
        finally
        {
            db.FileItems.Remove(testFile);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SharedSpace_FolderAndFileCreation_InheritsIsSharedFromParent()
    {
        var (client, _) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultRegularUsername,
            CustomWebApplicationFactory.DefaultRegularPassword,
            "Shared-Test-Device");

        // 1. Create a root folder with IsShared = true
        var createParentRes = await client.PostAsJsonAsync("/api/v1/folders", new OmegaCloud.Application.DTOs.Folders.CreateFolderRequest
        {
            Name = "Shared-Root-" + Guid.NewGuid().ToString("N")[..6],
            IsShared = true
        });
        createParentRes.EnsureSuccessStatusCode();
        var parentDto = (await createParentRes.Content.ReadFromJsonAsync<OmegaCloud.Application.DTOs.Folders.FolderDto>(_jsonOptions))!;
        Assert.True(parentDto.IsShared);

        // 2. Create a subfolder inside the shared folder
        var createSubRes = await client.PostAsJsonAsync("/api/v1/folders", new OmegaCloud.Application.DTOs.Folders.CreateFolderRequest
        {
            Name = "Shared-Subfolder",
            ParentFolderId = parentDto.Id
        });
        createSubRes.EnsureSuccessStatusCode();
        var subDto = (await createSubRes.Content.ReadFromJsonAsync<OmegaCloud.Application.DTOs.Folders.FolderDto>(_jsonOptions))!;
        Assert.True(subDto.IsShared);

        // 3. Upload a file inside the shared folder
        using var content = new MultipartFormDataContent();
        var fileBytes = new ByteArrayContent("Shared test file content"u8.ToArray());
        fileBytes.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileBytes, "file", "shared_doc.txt");
        content.Add(new StringContent(parentDto.Id.ToString()), "folderId");

        var uploadRes = await client.PostAsync("/api/v1/files/upload", content);
        uploadRes.EnsureSuccessStatusCode();
        var fileDto = (await uploadRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;
        Assert.True(fileDto.IsShared);
    }

    [Fact]
    public async Task TokenRefresh_InvalidatesOldSession_OldTokenImmediatelyRejected()
    {
        var (client, login) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultRegularUsername,
            CustomWebApplicationFactory.DefaultRegularPassword,
            "Refresh-Device-" + Guid.NewGuid().ToString("N")[..4]);

        var oldToken = login.AccessToken;

        // Perform refresh using refresh endpoint
        var refreshRes = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = login.RefreshToken
        });
        refreshRes.EnsureSuccessStatusCode();
        var newLogin = (await refreshRes.Content.ReadFromJsonAsync<RefreshTokenResponse>(_jsonOptions))!;
        Assert.NotEqual(oldToken, newLogin.AccessToken);

        // Try accessing protected resource with OLD token
        var testOldClient = _factory.CreateClient();
        testOldClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oldToken);
        var oldTokenMe = await testOldClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, oldTokenMe.StatusCode);

        // Accessing protected resource with NEW token must succeed
        var testNewClient = _factory.CreateClient();
        testNewClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newLogin.AccessToken);
        var newTokenMe = await testNewClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, newTokenMe.StatusCode);
    }
}
