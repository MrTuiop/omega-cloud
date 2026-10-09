using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OmegaCloud.Application.DTOs.Admin;
using OmegaCloud.Application.DTOs.Auth;

namespace OmegaCloud.Tests.Integration;

public class AdminApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AdminApiTests(CustomWebApplicationFactory factory)
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
    public async Task AdminUsers_RequiresAdminRole()
    {
        var anonymousClient = _factory.CreateClient();
        var anonRes = await anonymousClient.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, anonRes.StatusCode);

        var userClient = await CreateRegularUserClientAsync();
        var userRes = await userClient.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, userRes.StatusCode);

        var adminClient = await CreateAdminClientAsync();
        var adminRes = await adminClient.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.OK, adminRes.StatusCode);
    }

    [Fact]
    public async Task AdminUsers_CrudWorkflow_Succeeds()
    {
        var client = await CreateAdminClientAsync();

        // 1. Get users list
        var listRes = await client.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var users = await listRes.Content.ReadFromJsonAsync<List<AdminUserDto>>(_jsonOptions);
        Assert.NotNull(users);
        Assert.NotEmpty(users);

        // 2. Create user
        var newUsername = "testuser_" + Guid.NewGuid().ToString("N")[..8];
        var createRes = await client.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Username = newUsername,
            Email = $"{newUsername}@test.local",
            Password = "TestPassword123!",
            Role = "User",
            AllocatedQuotaBytes = 50L * 1024 * 1024 * 1024
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var createdUser = await createRes.Content.ReadFromJsonAsync<AdminUserDto>(_jsonOptions);
        Assert.NotNull(createdUser);
        Assert.Equal(newUsername, createdUser.Username);

        // 3. Get user by ID
        var getRes = await client.GetAsync($"/api/v1/admin/users/{createdUser.Id}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var detailUser = await getRes.Content.ReadFromJsonAsync<AdminUserDetailDto>(_jsonOptions);
        Assert.NotNull(detailUser);
        Assert.Equal(createdUser.Id, detailUser.Id);

        // 4. Update user quota and role
        var patchRes = await client.PatchAsJsonAsync($"/api/v1/admin/users/{createdUser.Id}", new UpdateUserRequest
        {
            Role = "Admin",
            AllocatedQuotaBytes = 200L * 1024 * 1024 * 1024,
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);
        var updatedUser = await patchRes.Content.ReadFromJsonAsync<AdminUserDto>(_jsonOptions);
        Assert.NotNull(updatedUser);
        Assert.Equal("Admin", updatedUser.Role);
        Assert.Equal(200L * 1024 * 1024 * 1024, updatedUser.AllocatedQuotaBytes);

        // 5. Reset password
        var resetRes = await client.PostAsJsonAsync($"/api/v1/admin/users/{createdUser.Id}/reset-password", new AdminResetPasswordRequest
        {
            NewPassword = "BrandNewPassword123!"
        });
        Assert.Equal(HttpStatusCode.OK, resetRes.StatusCode);

        // 6. Revoke sessions
        var revokeRes = await client.DeleteAsync($"/api/v1/admin/users/{createdUser.Id}/sessions");
        Assert.Equal(HttpStatusCode.OK, revokeRes.StatusCode);

        // 7. Delete user
        var deleteRes = await client.DeleteAsync($"/api/v1/admin/users/{createdUser.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        // 8. Confirm deleted
        var checkRes = await client.GetAsync($"/api/v1/admin/users/{createdUser.Id}");
        Assert.Equal(HttpStatusCode.NotFound, checkRes.StatusCode);
    }

    [Fact]
    public async Task AdminAudit_RequiresAdminRole_AndReturnsLogs()
    {
        var userClient = await CreateRegularUserClientAsync();
        var forbidRes = await userClient.GetAsync("/api/v1/admin/audit");
        Assert.Equal(HttpStatusCode.Forbidden, forbidRes.StatusCode);

        var adminClient = await CreateAdminClientAsync();
        var auditRes = await adminClient.GetAsync("/api/v1/admin/audit");
        Assert.Equal(HttpStatusCode.OK, auditRes.StatusCode);
        var logs = await auditRes.Content.ReadFromJsonAsync<List<AuditLogDto>>(_jsonOptions);
        Assert.NotNull(logs);
        Assert.NotEmpty(logs); // login was recorded!
        Assert.Contains(logs, l => l.Action == "UserLogin");
    }

    [Fact]
    public async Task AdminHealth_RequiresAdminRole_AndReturnsMetrics()
    {
        var userClient = await CreateRegularUserClientAsync();
        var forbidRes = await userClient.GetAsync("/api/v1/admin/health");
        Assert.Equal(HttpStatusCode.Forbidden, forbidRes.StatusCode);

        var adminClient = await CreateAdminClientAsync();
        var healthRes = await adminClient.GetAsync("/api/v1/admin/health");
        Assert.Equal(HttpStatusCode.OK, healthRes.StatusCode);
        var health = await healthRes.Content.ReadFromJsonAsync<SystemHealthDto>(_jsonOptions);
        Assert.NotNull(health);
        Assert.Equal("Healthy", health.DatabaseStatus);
        Assert.True(health.TotalUsers > 0);
        Assert.True(health.TotalNodes > 0);
        Assert.True(health.TotalMemoryBytes > 0);
    }

    [Fact]
    public async Task AdminUsers_RevokeSingleSession_Succeeds()
    {
        var adminClient = await CreateAdminClientAsync();

        // 1. Create a user
        var newUsername = "sessionuser_" + Guid.NewGuid().ToString("N")[..8];
        var createRes = await adminClient.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Username = newUsername,
            Email = $"{newUsername}@test.local",
            Password = "TestPassword123!",
            Role = "User",
            AllocatedQuotaBytes = 10L * 1024 * 1024 * 1024
        });
        createRes.EnsureSuccessStatusCode();
        var user = await createRes.Content.ReadFromJsonAsync<AdminUserDto>(_jsonOptions);
        Assert.NotNull(user);

        // 2. User logs in to create a session
        var userClient = _factory.CreateClient();
        var loginRes = await userClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            UsernameOrEmail = newUsername,
            Password = "TestPassword123!",
            DeviceName = "Session-Device-1"
        });
        loginRes.EnsureSuccessStatusCode();

        // 3. Inspect user details to get session ID
        var detailRes = await adminClient.GetAsync($"/api/v1/admin/users/{user.Id}");
        detailRes.EnsureSuccessStatusCode();
        var detail = await detailRes.Content.ReadFromJsonAsync<AdminUserDetailDto>(_jsonOptions);
        Assert.NotNull(detail);
        Assert.NotEmpty(detail.Sessions);
        var targetSession = detail.Sessions.First();

        // 4. Admin revokes this specific session
        var revokeRes = await adminClient.DeleteAsync($"/api/v1/admin/users/{user.Id}/sessions/{targetSession.Id}");
        Assert.Equal(HttpStatusCode.OK, revokeRes.StatusCode);

        // 5. Confirm session is no longer active
        var verifyRes = await adminClient.GetAsync($"/api/v1/admin/users/{user.Id}");
        var verifyDetail = await verifyRes.Content.ReadFromJsonAsync<AdminUserDetailDto>(_jsonOptions);
        Assert.NotNull(verifyDetail);
        Assert.Equal(0, verifyDetail.ActiveSessionsCount);

        // 6. Check audit log has SessionRevoked
        var auditRes = await adminClient.GetAsync("/api/v1/admin/audit?action=SessionRevoked");
        Assert.Equal(HttpStatusCode.OK, auditRes.StatusCode);
        var auditLogs = await auditRes.Content.ReadFromJsonAsync<List<AuditLogDto>>(_jsonOptions);
        Assert.NotNull(auditLogs);
        Assert.Contains(auditLogs, l => l.Action == "SessionRevoked" && l.ResourceId == targetSession.Id.ToString());
    }

    [Fact]
    public async Task AdminUsers_Validation_RejectsNegativeQuotaAndInvalidRole()
    {
        var adminClient = await CreateAdminClientAsync();

        // Negative quota on create
        var invalidCreate = await adminClient.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Username = "baduser_" + Guid.NewGuid().ToString("N")[..8],
            Email = "bad@test.local",
            Password = "ValidPassword123!",
            Role = "User",
            AllocatedQuotaBytes = -100
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCreate.StatusCode);

        // Invalid role on create
        var invalidRoleCreate = await adminClient.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Username = "badrole_" + Guid.NewGuid().ToString("N")[..8],
            Email = "badrole@test.local",
            Password = "ValidPassword123!",
            Role = "SuperHacker"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidRoleCreate.StatusCode);
    }

    [Fact]
    public async Task AdminUsers_PreventsAdminFromDeactivatingOrDemotingSelf()
    {
        var adminClient = await CreateAdminClientAsync();

        // Get admin user profile to find ID
        var meRes = await adminClient.GetAsync("/api/v1/auth/me");
        meRes.EnsureSuccessStatusCode();
        var me = await meRes.Content.ReadFromJsonAsync<UserProfileDto>(_jsonOptions);
        Assert.NotNull(me);

        // Try deactivating self
        var deactivateRes = await adminClient.PatchAsJsonAsync($"/api/v1/admin/users/{me.Id}", new UpdateUserRequest
        {
            IsActive = false
        });
        Assert.Equal(HttpStatusCode.BadRequest, deactivateRes.StatusCode);

        // Try demoting self
        var demoteRes = await adminClient.PatchAsJsonAsync($"/api/v1/admin/users/{me.Id}", new UpdateUserRequest
        {
            Role = "User"
        });
        Assert.Equal(HttpStatusCode.BadRequest, demoteRes.StatusCode);
    }
}
