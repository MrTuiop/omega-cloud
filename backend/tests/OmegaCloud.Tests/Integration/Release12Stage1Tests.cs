using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Admin;
using OmegaCloud.Application.DTOs.Auth;
using OmegaCloud.Application.DTOs.Nodes;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Domain.Enums;
using OmegaCloud.Infrastructure.Data;
using Xunit;

namespace OmegaCloud.Tests.Integration;

public class Release12Stage1Tests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Release12Stage1Tests(CustomWebApplicationFactory factory)
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
            DeviceName = "Admin-Release12-Device"
        });
        loginResponse.EnsureSuccessStatusCode();

        var loginResult = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);
        return client;
    }

    [Fact]
    public async Task MasterLocalStorageNode_ReturnsRealPhysicalDiskTelemetry()
    {
        var adminClient = await CreateAdminClientAsync();

        var response = await adminClient.GetAsync("/api/v1/admin/nodes");
        response.EnsureSuccessStatusCode();

        var nodes = (await response.Content.ReadFromJsonAsync<List<StorageNodeDto>>(_jsonOptions))!;
        var localNode = nodes.FirstOrDefault(n => n.Type == "LocalDisk");

        Assert.NotNull(localNode);
        Assert.True(localNode.TotalSpaceBytes > 0, "TotalSpaceBytes should be greater than 0");
        Assert.True(localNode.AvailableSpaceBytes >= 0, "AvailableSpaceBytes should be >= 0");
        Assert.True(localNode.AvailableSpaceBytes <= localNode.TotalSpaceBytes, "AvailableSpaceBytes must not exceed TotalSpaceBytes");

        // Verify single node fetch also returns fresh telemetry
        var singleResponse = await adminClient.GetAsync($"/api/v1/admin/nodes/{localNode.Id}");
        singleResponse.EnsureSuccessStatusCode();
        var singleNode = (await singleResponse.Content.ReadFromJsonAsync<StorageNodeDto>(_jsonOptions))!;

        Assert.Equal(localNode.TotalSpaceBytes, singleNode.TotalSpaceBytes);
        Assert.True(Math.Abs(localNode.AvailableSpaceBytes - singleNode.AvailableSpaceBytes) < 100_000_000L,
            "AvailableSpaceBytes between subsequent live queries should be approximately equal");
    }

    [Fact]
    public async Task NodeCapacityUpdate_WithOversoldQuotas_SucceedsWhenFilesFit()
    {
        var adminClient = await CreateAdminClientAsync();

        // 1. Fetch current active nodes
        var nodesResponse = await adminClient.GetAsync("/api/v1/admin/nodes");
        nodesResponse.EnsureSuccessStatusCode();
        var nodes = (await nodesResponse.Content.ReadFromJsonAsync<List<StorageNodeDto>>(_jsonOptions))!;
        var localNode = nodes.First(n => n.Type == "LocalDisk");

        var totalActiveCapacity = nodes.Where(n => n.IsActive).Sum(n => n.MaxCapacityBytes);

        // 2. Create users with quotas whose sum clearly exceeds totalActiveCapacity (overprovisioning)
        // Each user takes 60% of total active capacity, so 2 users take 120%
        var quotaPerUser = (long)(totalActiveCapacity * 0.6);

        var user1Resp = await adminClient.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Username = "oversold_user_1_" + Guid.NewGuid().ToString("N")[..6],
            Email = $"oversold1_{Guid.NewGuid():N}@test.local",
            Password = "Password123!",
            Role = "User",
            AllocatedQuotaBytes = quotaPerUser
        });
        user1Resp.EnsureSuccessStatusCode();
        var user1 = (await user1Resp.Content.ReadFromJsonAsync<AdminUserDto>(_jsonOptions))!;

        var user2Resp = await adminClient.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Username = "oversold_user_2_" + Guid.NewGuid().ToString("N")[..6],
            Email = $"oversold2_{Guid.NewGuid():N}@test.local",
            Password = "Password123!",
            Role = "User",
            AllocatedQuotaBytes = quotaPerUser
        });
        user2Resp.EnsureSuccessStatusCode();
        var user2 = (await user2Resp.Content.ReadFromJsonAsync<AdminUserDto>(_jsonOptions))!;

        try
        {
            // 3. Act: Update local node capacity.
            // Even though total allocated quotas (user1 + user2 + admin) exceeds active capacity,
            // update should SUCCEED because real used files are 0 (or fit).
            var newCapacity = (long)(localNode.TotalSpaceBytes * 0.7);
            if (newCapacity <= 0)
            {
                newCapacity = localNode.MaxCapacityBytes;
            }

            var patchResponse = await adminClient.PatchAsJsonAsync($"/api/v1/admin/nodes/{localNode.Id}", new UpdateStorageNodeRequest
            {
                MaxCapacityBytes = newCapacity
            });

            Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
            var updated = (await patchResponse.Content.ReadFromJsonAsync<StorageNodeDto>(_jsonOptions))!;
            Assert.Equal(newCapacity, updated.MaxCapacityBytes);
        }
        finally
        {
            // Clean up created users
            await adminClient.DeleteAsync($"/api/v1/admin/users/{user1.Id}");
            await adminClient.DeleteAsync($"/api/v1/admin/users/{user2.Id}");
        }
    }

    [Fact]
    public async Task NodeCapacityDownscale_BelowActuallyUsedBytes_IsRejected()
    {
        var adminClient = await CreateAdminClientAsync();

        var nodesResponse = await adminClient.GetAsync("/api/v1/admin/nodes");
        nodesResponse.EnsureSuccessStatusCode();
        var nodes = (await nodesResponse.Content.ReadFromJsonAsync<List<StorageNodeDto>>(_jsonOptions))!;
        var localNode = nodes.First(n => n.Type == "LocalDisk");

        // 1. Add a dummy file item in DB with 50 MB size
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstAsync(u => u.Role == UserRole.User);

        var testFile = new FileItem
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Name = "stage1_test_file_" + Guid.NewGuid().ToString("N")[..6] + ".bin",
            StorageBlobId = Guid.NewGuid(),
            SizeBytes = 50_000_000L, // 50 MB
            MimeType = "application/octet-stream",
            Extension = ".bin",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
        db.FileItems.Add(testFile);
        await db.SaveChangesAsync();

        try
        {
            // 2. Try to shrink localNode capacity to 10 MB (when other active capacity is 0 or less than 50 MB)
            var otherActiveCapacity = await db.StorageNodes
                .Where(n => n.Id != localNode.Id && n.IsActive)
                .SumAsync(n => n.MaxCapacityBytes);

            var totalUsed = await db.FileItems.Where(f => !f.IsDeleted).SumAsync(f => f.SizeBytes);

            // Set capacity such that otherActiveCapacity + requestedCapacity < totalUsed
            var requestedCapacity = Math.Max(0, totalUsed - otherActiveCapacity - 1_000_000L);

            var patchResponse = await adminClient.PatchAsJsonAsync($"/api/v1/admin/nodes/{localNode.Id}", new UpdateStorageNodeRequest
            {
                MaxCapacityBytes = requestedCapacity
            });

            Assert.Equal(HttpStatusCode.BadRequest, patchResponse.StatusCode);
            var body = await patchResponse.Content.ReadAsStringAsync();
            Assert.Contains("Невозможно уменьшить емкость нод ниже фактически занятого объема файлов", body);
        }
        finally
        {
            db.FileItems.Remove(testFile);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task MasterLocalStorageNode_CapacityExceedingPhysicalDisk_IsRejected()
    {
        var adminClient = await CreateAdminClientAsync();

        var nodesResponse = await adminClient.GetAsync("/api/v1/admin/nodes");
        nodesResponse.EnsureSuccessStatusCode();
        var nodes = (await nodesResponse.Content.ReadFromJsonAsync<List<StorageNodeDto>>(_jsonOptions))!;
        var localNode = nodes.First(n => n.Type == "LocalDisk");

        // Try setting MaxCapacityBytes greater than actual physical TotalSpaceBytes
        var excessiveCapacity = localNode.TotalSpaceBytes + 10_000_000_000L;

        var patchResponse = await adminClient.PatchAsJsonAsync($"/api/v1/admin/nodes/{localNode.Id}", new UpdateStorageNodeRequest
        {
            MaxCapacityBytes = excessiveCapacity
        });

        Assert.Equal(HttpStatusCode.BadRequest, patchResponse.StatusCode);
        var body = await patchResponse.Content.ReadAsStringAsync();
        Assert.Contains("не может превышать физический размер диска", body);
    }

    [Fact]
    public async Task AdminUser_QuotaValidation_SupportsOverselling_RejectsSingleUserExceedingCluster()
    {
        var adminClient = await CreateAdminClientAsync();

        var nodesResponse = await adminClient.GetAsync("/api/v1/admin/nodes");
        nodesResponse.EnsureSuccessStatusCode();
        var nodes = (await nodesResponse.Content.ReadFromJsonAsync<List<StorageNodeDto>>(_jsonOptions))!;
        var totalActiveCapacity = nodes.Where(n => n.IsActive).Sum(n => n.MaxCapacityBytes);

        // 1. Single user requesting more than totalActiveCapacity is rejected
        var singleExceeding = totalActiveCapacity + 1_000_000L;
        var failResp = await adminClient.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Username = "too_large_quota_" + Guid.NewGuid().ToString("N")[..6],
            Email = $"toolarge_{Guid.NewGuid():N}@test.local",
            Password = "ValidPassword123!",
            Role = "User",
            AllocatedQuotaBytes = singleExceeding
        });

        Assert.Equal(HttpStatusCode.BadRequest, failResp.StatusCode);
        var failBody = await failResp.Content.ReadAsStringAsync();
        Assert.Contains("would exceed active storage nodes capacity", failBody);

        // 2. Single user requesting valid quota within totalActiveCapacity succeeds
        var validQuota = (long)(totalActiveCapacity * 0.8);
        var okResp = await adminClient.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Username = "valid_oversell_" + Guid.NewGuid().ToString("N")[..6],
            Email = $"validoversell_{Guid.NewGuid():N}@test.local",
            Password = "ValidPassword123!",
            Role = "User",
            AllocatedQuotaBytes = validQuota
        });

        Assert.Equal(HttpStatusCode.Created, okResp.StatusCode);
        var createdUser = (await okResp.Content.ReadFromJsonAsync<AdminUserDto>(_jsonOptions))!;

        try
        {
            // 3. Updating quota exceeding totalActiveCapacity is rejected
            var patchExceedResp = await adminClient.PatchAsJsonAsync($"/api/v1/admin/users/{createdUser.Id}", new UpdateUserRequest
            {
                AllocatedQuotaBytes = singleExceeding
            });
            Assert.Equal(HttpStatusCode.BadRequest, patchExceedResp.StatusCode);

            // 4. Updating quota within totalActiveCapacity succeeds even with existing other users
            var patchOkResp = await adminClient.PatchAsJsonAsync($"/api/v1/admin/users/{createdUser.Id}", new UpdateUserRequest
            {
                AllocatedQuotaBytes = (long)(totalActiveCapacity * 0.9)
            });
            Assert.Equal(HttpStatusCode.OK, patchOkResp.StatusCode);
        }
        finally
        {
            await adminClient.DeleteAsync($"/api/v1/admin/users/{createdUser.Id}");
        }
    }

    [Fact]
    public async Task MasterLocalStorageNode_StaysOnlineAndZeroLag_EvenWithStaleHeartbeatTimestamp()
    {
        var adminClient = await CreateAdminClientAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var localNode = await db.StorageNodes.FirstAsync(n => n.Type == StorageNodeType.LocalDisk);

        // Artificially set LastHeartbeatAt 2 hours in the past
        localNode.LastHeartbeatAt = DateTime.UtcNow.AddHours(-2);
        await db.SaveChangesAsync();

        var nodesResponse = await adminClient.GetAsync("/api/v1/admin/nodes");
        nodesResponse.EnsureSuccessStatusCode();
        var nodes = (await nodesResponse.Content.ReadFromJsonAsync<List<StorageNodeDto>>(_jsonOptions))!;
        var localDto = nodes.First(n => n.Type == "LocalDisk");

        Assert.True(localDto.IsOnline, "Master local node must stay online even if no external heartbeats are sent");
        Assert.Equal(0, localDto.SecondsSinceLastHeartbeat);
    }

    [Fact]
    public async Task AdminUser_CreateWithoutAllocatedQuota_GracefullySucceedsAndClampsToClusterCapacity()
    {
        var adminClient = await CreateAdminClientAsync();

        var nodesResponse = await adminClient.GetAsync("/api/v1/admin/nodes");
        nodesResponse.EnsureSuccessStatusCode();
        var nodes = (await nodesResponse.Content.ReadFromJsonAsync<List<StorageNodeDto>>(_jsonOptions))!;
        var totalActiveCapacity = nodes.Where(n => n.IsActive).Sum(n => n.MaxCapacityBytes);

        var username = "default_quota_user_" + Guid.NewGuid().ToString("N")[..6];
        var response = await adminClient.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Username = username,
            Email = $"{username}@test.local",
            Password = "ValidPassword123!",
            Role = "User"
            // AllocatedQuotaBytes omitted -> should default safely without failing
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var createdUser = (await response.Content.ReadFromJsonAsync<AdminUserDto>(_jsonOptions))!;
        Assert.True(createdUser.AllocatedQuotaBytes <= totalActiveCapacity);

        await adminClient.DeleteAsync($"/api/v1/admin/users/{createdUser.Id}");
    }
}

