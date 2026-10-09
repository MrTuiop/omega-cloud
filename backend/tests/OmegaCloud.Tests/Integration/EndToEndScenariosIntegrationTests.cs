using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Admin;
using OmegaCloud.Application.DTOs.Auth;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Application.DTOs.Folders;
using OmegaCloud.Application.DTOs.Nodes;
using OmegaCloud.Application.DTOs.Trash;
using OmegaCloud.Application.DTOs.Upload;

namespace OmegaCloud.Tests.Integration;

public class EndToEndScenariosIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public EndToEndScenariosIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string username, string password, string deviceName)
    {
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            UsernameOrEmail = username,
            Password = password,
            DeviceName = deviceName
        });
        loginResponse.EnsureSuccessStatusCode();

        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions);
        Assert.NotNull(loginResult?.AccessToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);
        return client;
    }

    private Task<HttpClient> CreateRegularUserClientAsync(string device = "E2E-User-Agent") =>
        CreateAuthenticatedClientAsync(CustomWebApplicationFactory.DefaultRegularUsername, CustomWebApplicationFactory.DefaultRegularPassword, device);

    private Task<HttpClient> CreateAdminClientAsync(string device = "E2E-Admin-Agent") =>
        CreateAuthenticatedClientAsync(CustomWebApplicationFactory.DefaultAdminUsername, CustomWebApplicationFactory.DefaultAdminPassword, device);

    [Fact]
    public async Task Scenario1_User_CompleteLifecycle_UploadNavigateModifyTrashRestoreAndDownload()
    {
        var client = await CreateRegularUserClientAsync();

        // 1. Verify User Profile
        var meResponse = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<UserProfileDto>(_jsonOptions);
        Assert.NotNull(me);
        Assert.Equal(CustomWebApplicationFactory.DefaultRegularUsername, me.Username);

        // 2. Create Folder Hierarchy (/E2E_Docs -> /E2E_Docs/Projects)
        var createFolderResponse = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest
        {
            Name = "E2E_Docs_" + Guid.NewGuid().ToString("N")[..6],
            Color = "blue"
        });
        Assert.Equal(HttpStatusCode.Created, createFolderResponse.StatusCode);
        var rootFolder = await createFolderResponse.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions);
        Assert.NotNull(rootFolder);

        var createSubfolderResponse = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest
        {
            Name = "Projects",
            ParentFolderId = rootFolder.Id,
            Color = "purple"
        });
        Assert.Equal(HttpStatusCode.Created, createSubfolderResponse.StatusCode);
        var subFolder = await createSubfolderResponse.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions);
        Assert.NotNull(subFolder);

        // 3. Direct Stream Upload (Small File)
        var fileContentText = "Hello Omega Cloud E2E Integration Test Content!";
        var fileBytes = Encoding.UTF8.GetBytes(fileContentText);
        var fileName = "notes_" + Guid.NewGuid().ToString("N")[..6] + ".txt";

        using var directContent = new ByteArrayContent(fileBytes);
        directContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        directContent.Headers.Add("X-File-Name", fileName);

        var uploadDirectResponse = await client.PostAsync("/api/v1/upload/direct-stream", directContent);
        Assert.Equal(HttpStatusCode.Created, uploadDirectResponse.StatusCode);
        var uploadedFile = await uploadDirectResponse.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions);
        Assert.NotNull(uploadedFile);
        Assert.Equal(fileName, uploadedFile.Name);
        Assert.Equal(fileBytes.Length, uploadedFile.SizeBytes);

        // 4. Resumable Chunk Upload (Chunk Session)
        var chunkData = Encoding.UTF8.GetBytes("This is chunked session payload testing full chunk assembly pipeline.");
        var chunkFileName = "archive_" + Guid.NewGuid().ToString("N")[..6] + ".bin";
        string chunkSha256;
        using (var sha = SHA256.Create())
        {
            chunkSha256 = Convert.ToHexString(sha.ComputeHash(chunkData)).ToLowerInvariant();
        }

        var initSessionResponse = await client.PostAsJsonAsync("/api/v1/upload/sessions/init", new InitUploadSessionRequest
        {
            FileName = chunkFileName,
            TotalSizeBytes = chunkData.Length,
            TotalChunks = 1,
            ChunkSizeBytes = chunkData.Length,
            ExpectedSha256 = chunkSha256,
            TargetFolderId = rootFolder.Id
        });
        Assert.Equal(HttpStatusCode.OK, initSessionResponse.StatusCode);
        var session = await initSessionResponse.Content.ReadFromJsonAsync<UploadSessionDto>(_jsonOptions);
        Assert.NotNull(session);

        // Upload chunk 0
        using var chunkContent = new ByteArrayContent(chunkData);
        chunkContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var putChunkResponse = await client.PutAsync($"/api/v1/upload/sessions/{session.SessionId}/chunks/0", chunkContent);
        Assert.Equal(HttpStatusCode.OK, putChunkResponse.StatusCode);

        // Complete chunk session
        var completeResponse = await client.PostAsync($"/api/v1/upload/sessions/{session.SessionId}/complete", null);
        Assert.Equal(HttpStatusCode.Created, completeResponse.StatusCode);
        var completedChunkFile = await completeResponse.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions);
        Assert.NotNull(completedChunkFile);
        Assert.Equal(chunkFileName, completedChunkFile.Name);

        // 5. Verify Folder Content Listing
        var rootFolderContentResponse = await client.GetAsync($"/api/v1/folders/{rootFolder.Id}");
        Assert.Equal(HttpStatusCode.OK, rootFolderContentResponse.StatusCode);
        var folderContent = await rootFolderContentResponse.Content.ReadFromJsonAsync<FolderContentDto>(_jsonOptions);
        Assert.NotNull(folderContent);
        Assert.Contains(folderContent.Folders, f => f.Id == subFolder.Id);
        Assert.Contains(folderContent.Files, f => f.Id == completedChunkFile.Id);

        // 6. Download Direct Stream Uploaded File and Verify Byte Integrity
        var downloadResponse = await client.GetAsync($"/api/v1/files/{uploadedFile.Id}/download");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        var downloadedBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(fileBytes, downloadedBytes);

        // 7. File Metadata Update & Move to SubFolder
        var newFileName = "renamed_" + fileName;
        var updateResponse = await client.PatchAsJsonAsync($"/api/v1/files/{uploadedFile.Id}", new UpdateFileRequest
        {
            Name = newFileName,
            FolderId = subFolder.Id,
            IsFavorite = true
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updatedFile = await updateResponse.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions);
        Assert.NotNull(updatedFile);
        Assert.Equal(newFileName, updatedFile.Name);
        Assert.Equal(subFolder.Id, updatedFile.FolderId);
        Assert.True(updatedFile.IsFavorite);

        // 8. Move File to Trash (Soft Delete)
        var trashFileResponse = await client.DeleteAsync($"/api/v1/files/{uploadedFile.Id}");
        Assert.Equal(HttpStatusCode.NoContent, trashFileResponse.StatusCode);

        // 9. Inspect Trash Bin
        var trashResponse = await client.GetAsync("/api/v1/trash");
        Assert.Equal(HttpStatusCode.OK, trashResponse.StatusCode);
        var trashContent = await trashResponse.Content.ReadFromJsonAsync<TrashContentDto>(_jsonOptions);
        Assert.NotNull(trashContent);
        Assert.Contains(trashContent.Files, f => f.Id == uploadedFile.Id);

        // 10. Restore File from Trash
        var restoreResponse = await client.PostAsync($"/api/v1/trash/restore/{uploadedFile.Id}", null);
        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);

        // Verify it's no longer in trash
        var trashAfterRestore = await client.GetFromJsonAsync<TrashContentDto>("/api/v1/trash", _jsonOptions);
        Assert.NotNull(trashAfterRestore);
        Assert.DoesNotContain(trashAfterRestore.Files, f => f.Id == uploadedFile.Id);

        // 11. Delete again and Permanently Delete
        await client.DeleteAsync($"/api/v1/files/{uploadedFile.Id}");
        var permDeleteResponse = await client.DeleteAsync($"/api/v1/trash/permanent/{uploadedFile.Id}");
        Assert.Equal(HttpStatusCode.NoContent, permDeleteResponse.StatusCode);

        // 12. Soft-delete Folder and Empty Trash
        await client.DeleteAsync($"/api/v1/folders/{rootFolder.Id}");
        var emptyTrashResponse = await client.DeleteAsync("/api/v1/trash/empty");
        Assert.Equal(HttpStatusCode.NoContent, emptyTrashResponse.StatusCode);

        var finalTrash = await client.GetFromJsonAsync<TrashContentDto>("/api/v1/trash", _jsonOptions);
        Assert.NotNull(finalTrash);
        Assert.Empty(finalTrash.Folders);
        Assert.Empty(finalTrash.Files);
    }

    [Fact]
    public async Task Scenario2_Admin_CompleteLifecycle_HealthNodesUsersAndAudit()
    {
        var adminClient = await CreateAdminClientAsync();

        // 1. Check System Health
        var healthResponse = await adminClient.GetAsync("/api/v1/admin/health");
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        var health = await healthResponse.Content.ReadFromJsonAsync<SystemHealthDto>(_jsonOptions);
        Assert.NotNull(health);
        Assert.Equal("Healthy", health.DatabaseStatus);
        Assert.True(health.TotalUsers > 0);

        // 2. Manage Users (List and Quota update)
        var usersResponse = await adminClient.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.OK, usersResponse.StatusCode);
        var users = await usersResponse.Content.ReadFromJsonAsync<List<AdminUserDto>>(_jsonOptions);
        Assert.NotNull(users);
        var targetUser = users.FirstOrDefault(u => u.Username == CustomWebApplicationFactory.DefaultRegularUsername);
        Assert.NotNull(targetUser);

        var newQuota = targetUser.AllocatedQuotaBytes + 1_048_576L;
        var updateQuotaResponse = await adminClient.PatchAsJsonAsync($"/api/v1/admin/users/{targetUser.Id}", new UpdateUserRequest
        {
            AllocatedQuotaBytes = newQuota,
            Role = targetUser.Role,
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.OK, updateQuotaResponse.StatusCode);

        // 3. Admin Storage Nodes Management
        var nodesResponse = await adminClient.GetAsync("/api/v1/admin/nodes");
        Assert.Equal(HttpStatusCode.OK, nodesResponse.StatusCode);
        var nodes = await nodesResponse.Content.ReadFromJsonAsync<List<StorageNodeDto>>(_jsonOptions);
        Assert.NotNull(nodes);
        Assert.Contains(nodes, n => n.Type == "LocalDisk");

        // 4. Generate Node Registration Token & Storage Daemon Lifecycle
        var genTokenResp = await adminClient.PostAsJsonAsync("/api/v1/admin/nodes/generate-token", new GenerateNodeTokenRequest
        {
            Name = "E2E-Daemon-NAS",
            BaseUrl = "https://nas.example.local:8443",
            IsBehindNat = false
        });
        Assert.Equal(HttpStatusCode.OK, genTokenResp.StatusCode);
        var tokenDto = await genTokenResp.Content.ReadFromJsonAsync<NodeRegistrationTokenDto>(_jsonOptions);
        Assert.NotNull(tokenDto?.RegistrationKey);

        // Storage daemon registers with master
        var daemonClient = _factory.CreateClient();
        var regResponse = await daemonClient.PostAsJsonAsync("/api/v1/nodes/register", new RegisterNodeRequest
        {
            RegistrationKey = tokenDto.RegistrationKey,
            Name = "E2E-Daemon-NAS-Registered",
            BaseUrl = "https://nas.example.local:8443",
            IsBehindNat = false,
            TotalSpaceBytes = 500_000_000_000L,
            AvailableSpaceBytes = 450_000_000_000L
        });
        Assert.Equal(HttpStatusCode.OK, regResponse.StatusCode);
        var regResult = await regResponse.Content.ReadFromJsonAsync<RegisterNodeResponse>(_jsonOptions);
        Assert.NotNull(regResult);

        // Storage daemon pushes heartbeat telemetry
        var signer = _factory.Services.GetRequiredService<IHmacSigner>();
        var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = signer.ComputeHmacSha256($"{regResult.NodeId}:{unixTimestamp}", regResult.NodeSecret);

        var heartbeatRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/nodes/heartbeat")
        {
            Content = JsonContent.Create(new NodeHeartbeatRequest
            {
                TotalSpaceBytes = 500_000_000_000L,
                AvailableSpaceBytes = 440_000_000_000L,
                CpuLoad = 12.5,
                DiskStatus = "Healthy"
            })
        };
        heartbeatRequest.Headers.Add("X-Node-Id", regResult.NodeId.ToString());
        heartbeatRequest.Headers.Add("X-Node-Timestamp", unixTimestamp.ToString());
        heartbeatRequest.Headers.Add("X-Node-Signature", signature);

        var heartbeatResponse = await daemonClient.SendAsync(heartbeatRequest);
        Assert.Equal(HttpStatusCode.OK, heartbeatResponse.StatusCode);

        // Admin verifies the registered daemon is now visible in the cluster
        var updatedNodes = await adminClient.GetFromJsonAsync<List<StorageNodeDto>>("/api/v1/admin/nodes", _jsonOptions);
        Assert.NotNull(updatedNodes);
        Assert.Contains(updatedNodes, n => n.Id == regResult.NodeId);

        // 5. Query Audit Logs
        var auditResponse = await adminClient.GetAsync("/api/v1/admin/audit");
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
        var auditLogs = await auditResponse.Content.ReadFromJsonAsync<List<AuditLogDto>>(_jsonOptions);
        Assert.NotNull(auditLogs);
        Assert.NotEmpty(auditLogs);
    }

    [Fact]
    public async Task Scenario3_PublicHealth_AndSwaggerRouting()
    {
        var client = _factory.CreateClient();

        // 1. Anonymous health probe for Docker / Caddy
        var healthRes = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, healthRes.StatusCode);
        var healthJson = await healthRes.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", healthJson);

        // 2. Swagger specification endpoint
        var swaggerRes = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, swaggerRes.StatusCode);
        var swaggerJson = await swaggerRes.Content.ReadAsStringAsync();
        Assert.Contains("Omega Cloud Master API", swaggerJson);
    }

    [Fact]
    public async Task Scenario4_UserSessions_MultiDevice_ListingAndRevocation()
    {
        // 1. Regular user logs in from primary device (e.g. Work-MacBook)
        var macClient = await CreateRegularUserClientAsync("Work-MacBook");

        // 2. Same regular user logs in from second device (e.g. Mobile-iPhone)
        var phoneClient = await CreateRegularUserClientAsync("Mobile-iPhone");

        // 3. Inspect active sessions from primary device
        var sessionsResponse = await macClient.GetAsync("/api/v1/auth/sessions");
        Assert.Equal(HttpStatusCode.OK, sessionsResponse.StatusCode);
        var sessions = await sessionsResponse.Content.ReadFromJsonAsync<List<UserSessionDto>>(_jsonOptions);
        Assert.NotNull(sessions);
        Assert.True(sessions.Count >= 2);

        var phoneSession = sessions.FirstOrDefault(s => s.DeviceName == "Mobile-iPhone");
        Assert.NotNull(phoneSession);
        Assert.False(phoneSession.IsCurrent);

        var macSession = sessions.FirstOrDefault(s => s.DeviceName == "Work-MacBook");
        Assert.NotNull(macSession);
        Assert.True(macSession.IsCurrent);

        // 4. Revoke the phone session remotely from the mac client
        var revokeResponse = await macClient.DeleteAsync($"/api/v1/auth/sessions/{phoneSession.Id}");
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        // 5. Verify the phone session is no longer in active sessions
        var updatedSessions = await macClient.GetFromJsonAsync<List<UserSessionDto>>("/api/v1/auth/sessions", _jsonOptions);
        Assert.NotNull(updatedSessions);
        Assert.DoesNotContain(updatedSessions, s => s.Id == phoneSession.Id);
    }

    [Fact]
    public async Task Scenario5_Security_UnauthorizedAndForbiddenAccess()
    {
        var anonymousClient = _factory.CreateClient();

        // 1. Anonymous requests to protected endpoints return 401 Unauthorized
        var unauthFolders = await anonymousClient.GetAsync("/api/v1/folders");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthFolders.StatusCode);

        var unauthQuota = await anonymousClient.GetAsync("/api/v1/upload/quota");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthQuota.StatusCode);

        var unauthAdmin = await anonymousClient.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthAdmin.StatusCode);

        // 2. Regular user requests to admin endpoints return 403 Forbidden
        var userClient = await CreateRegularUserClientAsync();

        var forbiddenUsers = await userClient.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenUsers.StatusCode);

        var forbiddenNodes = await userClient.GetAsync("/api/v1/admin/nodes");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenNodes.StatusCode);

        var forbiddenHealth = await userClient.GetAsync("/api/v1/admin/health");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenHealth.StatusCode);

        var forbiddenAudit = await userClient.GetAsync("/api/v1/admin/audit");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenAudit.StatusCode);
    }

    [Fact]
    public async Task Scenario6_ResumableUpload_EdgeCases_InvalidSession_PrematureComplete_AndAbort()
    {
        var client = await CreateRegularUserClientAsync();

        // 1. Uploading chunk to a non-existent session returns 404
        using var dummyChunk = new ByteArrayContent(Encoding.UTF8.GetBytes("orphan payload"));
        dummyChunk.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var orphanResponse = await client.PutAsync($"/api/v1/upload/sessions/{Guid.NewGuid()}/chunks/0", dummyChunk);
        Assert.Equal(HttpStatusCode.NotFound, orphanResponse.StatusCode);

        // 2. Initialize a 2-chunk upload session
        var initResponse = await client.PostAsJsonAsync("/api/v1/upload/sessions/init", new InitUploadSessionRequest
        {
            FileName = "multi_chunk_test.bin",
            TotalSizeBytes = 2048,
            TotalChunks = 2,
            ChunkSizeBytes = 1024
        });
        Assert.Equal(HttpStatusCode.OK, initResponse.StatusCode);
        var session = await initResponse.Content.ReadFromJsonAsync<UploadSessionDto>(_jsonOptions);
        Assert.NotNull(session);

        // 3. Completing session before uploading all chunks returns 400 BadRequest
        var prematureCompleteResponse = await client.PostAsync($"/api/v1/upload/sessions/{session.SessionId}/complete", null);
        Assert.Equal(HttpStatusCode.BadRequest, prematureCompleteResponse.StatusCode);

        // 4. Abort (DELETE) the upload session
        var abortResponse = await client.DeleteAsync($"/api/v1/upload/sessions/{session.SessionId}");
        Assert.Equal(HttpStatusCode.NoContent, abortResponse.StatusCode);

        // 5. Attempting to upload chunk to aborted session returns 404 or 400
        using var chunkContent = new ByteArrayContent(new byte[1024]);
        chunkContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var chunkAfterAbortResponse = await client.PutAsync($"/api/v1/upload/sessions/{session.SessionId}/chunks/0", chunkContent);
        Assert.True(chunkAfterAbortResponse.StatusCode == HttpStatusCode.NotFound || chunkAfterAbortResponse.StatusCode == HttpStatusCode.BadRequest);
    }
}
