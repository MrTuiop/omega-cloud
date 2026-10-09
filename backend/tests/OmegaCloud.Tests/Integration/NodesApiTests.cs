using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Auth;
using OmegaCloud.Application.DTOs.Nodes;
using OmegaCloud.Application.DTOs.Upload;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;
using OmegaCloud.Infrastructure.Data;

namespace OmegaCloud.Tests.Integration;

public class NodesApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public NodesApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            UsernameOrEmail = CustomWebApplicationFactory.DefaultAdminUsername,
            Password = CustomWebApplicationFactory.DefaultAdminPassword,
            DeviceName = "Admin-Test-Device"
        });
        loginResponse.EnsureSuccessStatusCode();

        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult!.AccessToken);
        return client;
    }

    private async Task<HttpClient> CreateRegularUserClientAsync()
    {
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            UsernameOrEmail = CustomWebApplicationFactory.DefaultRegularUsername,
            Password = CustomWebApplicationFactory.DefaultRegularPassword,
            DeviceName = "User-Test-Device"
        });
        loginResponse.EnsureSuccessStatusCode();

        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult!.AccessToken);
        return client;
    }

    [Fact]
    public async Task GetNodes_AuthorizationChecks_EnforcedCorrectly()
    {
        var anonClient = _factory.CreateClient();
        var anonResp = await anonClient.GetAsync("/api/v1/admin/nodes");
        Assert.Equal(HttpStatusCode.Unauthorized, anonResp.StatusCode);

        var userClient = await CreateRegularUserClientAsync();
        var userResp = await userClient.GetAsync("/api/v1/admin/nodes");
        Assert.Equal(HttpStatusCode.Forbidden, userResp.StatusCode);

        var adminClient = await CreateAdminClientAsync();
        var adminResp = await adminClient.GetAsync("/api/v1/admin/nodes");
        Assert.Equal(HttpStatusCode.OK, adminResp.StatusCode);

        var nodes = await adminResp.Content.ReadFromJsonAsync<List<StorageNodeDto>>(_jsonOptions);
        Assert.NotNull(nodes);
        Assert.Contains(nodes, n => n.Type == "LocalDisk");
    }

    [Fact]
    public async Task FullNodeLifecycle_Token_Register_Heartbeat_DirectUpload_Delete()
    {
        var adminClient = await CreateAdminClientAsync();
        var anonClient = _factory.CreateClient();

        // 1. Admin generates token
        var genTokenResp = await adminClient.PostAsJsonAsync("/api/v1/admin/nodes/generate-token", new GenerateNodeTokenRequest
        {
            Name = "Integration-NAS",
            BaseUrl = "https://nas.example.local:8443",
            IsBehindNat = false
        });
        genTokenResp.EnsureSuccessStatusCode();
        var tokenDto = await genTokenResp.Content.ReadFromJsonAsync<NodeRegistrationTokenDto>(_jsonOptions);
        Assert.NotNull(tokenDto);
        Assert.NotEmpty(tokenDto.RegistrationKey);

        // 2. Storage daemon registers with token
        var registerResp = await anonClient.PostAsJsonAsync("/api/v1/nodes/register", new RegisterNodeRequest
        {
            RegistrationKey = tokenDto.RegistrationKey,
            Name = "Integration-NAS-Registered",
            BaseUrl = "https://nas.example.local:8443",
            IsBehindNat = false,
            TotalSpaceBytes = 1_000_000_000L,
            AvailableSpaceBytes = 900_000_000L
        });
        registerResp.EnsureSuccessStatusCode();
        var regDto = await registerResp.Content.ReadFromJsonAsync<RegisterNodeResponse>(_jsonOptions);
        Assert.NotNull(regDto);
        Assert.Equal(tokenDto.NodeId, regDto.NodeId);
        Assert.NotEmpty(regDto.NodeSecret);

        // 3. Re-registration fails
        var duplicateRegResp = await anonClient.PostAsJsonAsync("/api/v1/nodes/register", new RegisterNodeRequest
        {
            RegistrationKey = tokenDto.RegistrationKey
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateRegResp.StatusCode);

        // 4. Daemon sends heartbeat with HMAC signature
        var signer = _factory.Services.GetRequiredService<IHmacSigner>();
        var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = signer.ComputeHmacSha256($"{regDto.NodeId}:{unixTimestamp}", regDto.NodeSecret);

        var heartbeatRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/nodes/heartbeat")
        {
            Content = JsonContent.Create(new NodeHeartbeatRequest
            {
                TotalSpaceBytes = 1_000_000_000L,
                AvailableSpaceBytes = 880_000_000L,
                CpuLoad = 15.2,
                DiskStatus = "Healthy"
            })
        };
        heartbeatRequest.Headers.Add("X-Node-Id", regDto.NodeId.ToString());
        heartbeatRequest.Headers.Add("X-Node-Timestamp", unixTimestamp.ToString());
        heartbeatRequest.Headers.Add("X-Node-Signature", signature);

        var heartbeatResp = await anonClient.SendAsync(heartbeatRequest);
        heartbeatResp.EnsureSuccessStatusCode();
        var hbDto = await heartbeatResp.Content.ReadFromJsonAsync<HeartbeatResponseDto>(_jsonOptions);
        Assert.NotNull(hbDto);
        Assert.Equal("Online", hbDto.Status);

        // 5. Heartbeat with invalid signature fails with 401
        var badHeartbeatRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/nodes/heartbeat")
        {
            Content = JsonContent.Create(new NodeHeartbeatRequest())
        };
        badHeartbeatRequest.Headers.Add("X-Node-Id", regDto.NodeId.ToString());
        badHeartbeatRequest.Headers.Add("X-Node-Timestamp", unixTimestamp.ToString());
        badHeartbeatRequest.Headers.Add("X-Node-Signature", "wrong_signature");

        var badHbResp = await anonClient.SendAsync(badHeartbeatRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, badHbResp.StatusCode);

        // 6. Direct Upload Session: User initiates upload session on this direct remote node
        var userClient = await CreateRegularUserClientAsync();
        var initSessionResp = await userClient.PostAsJsonAsync("/api/v1/upload/sessions/init", new InitUploadSessionRequest
        {
            FileName = "direct_test_file.iso",
            TotalSizeBytes = 100 * 1024 * 1024L,
            StorageNodeId = regDto.NodeId
        });
        initSessionResp.EnsureSuccessStatusCode();
        var sessionDto = await initSessionResp.Content.ReadFromJsonAsync<UploadSessionDto>(_jsonOptions);
        Assert.NotNull(sessionDto);
        Assert.Equal("Direct", sessionDto.UploadMode);
        Assert.NotNull(sessionDto.TargetUrl);
        Assert.StartsWith("https://nas.example.local:8443/api/v1/upload/", sessionDto.TargetUrl);
        Assert.Contains("signature=", sessionDto.TargetUrl);

        // 7. Direct Download: User downloads file placed on this node
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = db.Users.First(u => u.Username == CustomWebApplicationFactory.DefaultRegularUsername);

            var blob = new StorageBlob
            {
                Id = Guid.NewGuid(),
                StorageNodeId = regDto.NodeId,
                Sha256Hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                SizeBytes = 1024,
                StorageRelativePath = "blobs/e3/b0/test.blob",
                ReferenceCount = 1
            };
            db.StorageBlobs.Add(blob);

            var fileItem = new FileItem
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                StorageBlobId = blob.Id,
                Name = "direct_download_test.bin",
                Extension = "bin",
                MimeType = "application/octet-stream",
                SizeBytes = 1024
            };
            db.FileItems.Add(fileItem);
            db.SaveChanges();

            // Download with ?json=true
            var jsonDownloadResp = await userClient.GetAsync($"/api/v1/files/{fileItem.Id}/download?json=true");
            jsonDownloadResp.EnsureSuccessStatusCode();
            var jsonDoc = await jsonDownloadResp.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
            var redirectUrl = jsonDoc.GetProperty("redirectUrl").GetString();
            Assert.NotNull(redirectUrl);
            Assert.StartsWith("https://nas.example.local:8443/api/v1/download/", redirectUrl);
            Assert.Contains("signature=", redirectUrl);
        }

        // 8. Admin gets node details
        var getNodeResp = await adminClient.GetAsync($"/api/v1/admin/nodes/{regDto.NodeId}");
        getNodeResp.EnsureSuccessStatusCode();
        var nodeDetails = await getNodeResp.Content.ReadFromJsonAsync<StorageNodeDto>(_jsonOptions);
        Assert.NotNull(nodeDetails);
        Assert.Equal("Integration-NAS-Registered", nodeDetails.Name);
        Assert.Equal(880_000_000L, nodeDetails.AvailableSpaceBytes);
        Assert.Equal(15.2, nodeDetails.CpuLoad);
        Assert.Equal("Healthy", nodeDetails.DiskStatus);

        // 9. Admin deletes / archives node
        var deleteResp = await adminClient.DeleteAsync($"/api/v1/admin/nodes/{regDto.NodeId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);
    }

    [Fact]
    public async Task Admin_UpdateAndPingNode_DirectStreamUpload_Succeeds()
    {
        var adminClient = await CreateAdminClientAsync();
        var userClient = await CreateRegularUserClientAsync();

        // 1. Get Master local node
        var nodesResp = await adminClient.GetAsync("/api/v1/admin/nodes");
        nodesResp.EnsureSuccessStatusCode();
        var nodes = await nodesResp.Content.ReadFromJsonAsync<List<StorageNodeDto>>(_jsonOptions);
        var localNode = nodes!.First(n => n.Type == "LocalDisk");

        // 2. Ping local node
        var pingResp = await adminClient.PostAsync($"/api/v1/admin/nodes/{localNode.Id}/ping", null);
        pingResp.EnsureSuccessStatusCode();
        var pingDto = await pingResp.Content.ReadFromJsonAsync<NodePingResultDto>(_jsonOptions);
        Assert.NotNull(pingDto);
        Assert.True(pingDto.IsReachable);
        Assert.Equal(0, pingDto.LatencyMs);

        // 3. Admin generates token for remote node
        var genTokenResp = await adminClient.PostAsJsonAsync("/api/v1/admin/nodes/generate-token", new GenerateNodeTokenRequest
        {
            Name = "Pending Node"
        });
        genTokenResp.EnsureSuccessStatusCode();
        var tokenDto = await genTokenResp.Content.ReadFromJsonAsync<NodeRegistrationTokenDto>(_jsonOptions);

        // 4. Admin updates node parameters
        var patchResp = await adminClient.PatchAsJsonAsync($"/api/v1/admin/nodes/{tokenDto!.NodeId}", new UpdateStorageNodeRequest
        {
            Name = "Configured Remote Node",
            BaseUrl = "https://configured.nas:8080",
            IsBehindNat = true,
            Status = StorageNodeStatus.Maintenance
        });
        patchResp.EnsureSuccessStatusCode();
        var patched = await patchResp.Content.ReadFromJsonAsync<StorageNodeDto>(_jsonOptions);
        Assert.NotNull(patched);
        Assert.Equal("Configured Remote Node", patched.Name);
        Assert.Equal("https://configured.nas:8080", patched.BaseUrl);
        Assert.True(patched.IsBehindNat);
        Assert.Equal("Maintenance", patched.Status);

        // 5. User direct stream uploads targeting local node explicitly
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("hello direct stream"));
        content.Add(fileContent, "file", "target_node_test.txt");

        var uploadResp = await userClient.PostAsync($"/api/v1/upload/direct-stream?storageNodeId={localNode.Id}", content);
        uploadResp.EnsureSuccessStatusCode();
        var fileDto = await uploadResp.Content.ReadFromJsonAsync<OmegaCloud.Application.DTOs.Files.FileItemDto>(_jsonOptions);
        Assert.NotNull(fileDto);
        Assert.Equal("target_node_test.txt", fileDto.Name);

        // 6. User verifies upload session chunkIndex query parameter
        var initResp = await userClient.PostAsJsonAsync("/api/v1/upload/sessions/init", new InitUploadSessionRequest
        {
            FileName = "chunk_index_query_test.bin",
            TotalSizeBytes = 50 * 1024 * 1024L,
            TotalChunks = 5
        });
        initResp.EnsureSuccessStatusCode();
        var session = await initResp.Content.ReadFromJsonAsync<UploadSessionDto>(_jsonOptions);

        var getSessionResp = await userClient.GetAsync($"/api/v1/upload/sessions/{session!.SessionId}?chunkIndex=2");
        getSessionResp.EnsureSuccessStatusCode();
        var sessionDto = await getSessionResp.Content.ReadFromJsonAsync<UploadSessionDto>(_jsonOptions);
        Assert.NotNull(sessionDto);
    }
}
