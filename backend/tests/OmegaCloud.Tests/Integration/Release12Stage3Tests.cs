using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OmegaCloud.Application.DTOs.Admin;
using OmegaCloud.Application.DTOs.Auth;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Application.DTOs.Folders;
using OmegaCloud.Domain.Entities;
using OmegaCloud.Infrastructure.Data;
using Xunit;

namespace OmegaCloud.Tests.Integration;

public class Release12Stage3Tests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Release12Stage3Tests(CustomWebApplicationFactory factory)
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

    private async Task<(HttpClient Client, LoginResponse Login, Guid UserId)> CreateNewUserClientAsync(long allocatedQuota = 5L * 1024 * 1024 * 1024)
    {
        var (adminClient, _) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultAdminUsername,
            CustomWebApplicationFactory.DefaultAdminPassword,
            "Admin-Setup-" + Guid.NewGuid().ToString("N")[..4]);

        var username = "user_" + Guid.NewGuid().ToString("N")[..8];
        var password = "UserPassword123!";
        var email = $"{username}@test.local";

        var createRes = await adminClient.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Username = username,
            Email = email,
            Password = password,
            Role = "User",
            AllocatedQuotaBytes = allocatedQuota
        });
        createRes.EnsureSuccessStatusCode();
        var createdUser = (await createRes.Content.ReadFromJsonAsync<AdminUserDto>(_jsonOptions))!;

        var (client, login) = await CreateClientForUserAsync(username, password, "Device-" + username);
        return (client, login, createdUser.Id);
    }

    private async Task<FolderDto> CreatePersonalFolderAsync(HttpClient client, string name, Guid? parentFolderId = null)
    {
        var res = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest
        {
            Name = name,
            ParentFolderId = parentFolderId
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions))!;
    }

    private async Task<FolderDto> CreateSharedFolderAsync(HttpClient client, string name, Guid? parentFolderId = null)
    {
        var res = await client.PostAsJsonAsync("/api/v1/shared/folders", new CreateFolderRequest
        {
            Name = name,
            ParentFolderId = parentFolderId,
            IsShared = true
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions))!;
    }

    private async Task<FileItemDto> UploadPersonalTestFileAsync(HttpClient client, string fileName, byte[] content, Guid? folderId = null)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", fileName);

        if (folderId.HasValue)
        {
            form.Add(new StringContent(folderId.Value.ToString()), "folderId");
        }

        var res = await client.PostAsync("/api/v1/files/upload", form);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;
    }

    private async Task<FileItemDto> UploadSharedTestFileAsync(HttpClient client, string fileName, byte[] content, Guid? folderId = null)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", fileName);

        if (folderId.HasValue)
        {
            form.Add(new StringContent(folderId.Value.ToString()), "folderId");
        }

        var res = await client.PostAsync("/api/v1/shared/upload", form);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;
    }

    [Fact]
    public async Task GetFolderTree_ReturnsPersonalAndSharedFolders_ExcludesDeletedAndOtherUsers()
    {
        var (user1Client, _, _) = await CreateNewUserClientAsync();
        var (user2Client, _, _) = await CreateNewUserClientAsync();

        // User 1 creates folders
        var u1Root = await CreatePersonalFolderAsync(user1Client, "U1_Personal_Root");
        var u1Child = await CreatePersonalFolderAsync(user1Client, "U1_Personal_Child", u1Root.Id);
        var u1Deleted = await CreatePersonalFolderAsync(user1Client, "U1_Personal_Deleted");

        // Soft-delete u1Deleted
        var deleteRes = await user1Client.DeleteAsync($"/api/v1/folders/{u1Deleted.Id}");
        deleteRes.EnsureSuccessStatusCode();

        // User 2 creates folder
        await CreatePersonalFolderAsync(user2Client, "U2_Personal_Secret");

        // User 1 creates shared folder
        var sharedRoot = await CreateSharedFolderAsync(user1Client, "Shared_Root_Global");
        var sharedChild = await CreateSharedFolderAsync(user1Client, "Shared_Child_Global", sharedRoot.Id);

        // Fetch tree as User 1
        var treeRes = await user1Client.GetAsync("/api/v1/folders/tree");
        treeRes.EnsureSuccessStatusCode();
        var tree = (await treeRes.Content.ReadFromJsonAsync<FolderTreeResponseDto>(_jsonOptions))!;

        Assert.NotNull(tree);
        // Verify personal folders
        Assert.Contains(tree.PersonalFolders, f => f.Id == u1Root.Id && f.Name == "U1_Personal_Root");
        Assert.Contains(tree.PersonalFolders, f => f.Id == u1Child.Id && f.ParentFolderId == u1Root.Id);
        Assert.DoesNotContain(tree.PersonalFolders, f => f.Id == u1Deleted.Id);
        Assert.DoesNotContain(tree.PersonalFolders, f => f.Name == "U2_Personal_Secret");

        // Verify shared folders
        Assert.Contains(tree.SharedFolders, f => f.Id == sharedRoot.Id && f.Name == "Shared_Root_Global");
        Assert.Contains(tree.SharedFolders, f => f.Id == sharedChild.Id && f.ParentFolderId == sharedRoot.Id);
    }

    [Fact]
    public async Task Search_ScopeEverywhere_FindsBothPersonalAndSharedFiles()
    {
        var (client, _, _) = await CreateNewUserClientAsync();

        var query = "omgsearch_" + Guid.NewGuid().ToString("N")[..6];
        var pFile = await UploadPersonalTestFileAsync(client, $"{query}_personal.txt", "abc"u8.ToArray());
        var sFile = await UploadSharedTestFileAsync(client, $"{query}_shared.txt", "def"u8.ToArray());

        var searchRes = await client.GetAsync($"/api/v1/files/search?query={query}&scope=everywhere");
        searchRes.EnsureSuccessStatusCode();
        var result = (await searchRes.Content.ReadFromJsonAsync<FolderContentDto>(_jsonOptions))!;

        Assert.Contains(result.Files, f => f.Id == pFile.Id);
        Assert.Contains(result.Files, f => f.Id == sFile.Id);
    }

    [Fact]
    public async Task Search_ScopePersonal_FindsOnlyPersonalFiles()
    {
        var (client, _, _) = await CreateNewUserClientAsync();

        var query = "perscope_" + Guid.NewGuid().ToString("N")[..6];
        var pFile = await UploadPersonalTestFileAsync(client, $"{query}_personal.txt", "abc"u8.ToArray());
        var sFile = await UploadSharedTestFileAsync(client, $"{query}_shared.txt", "def"u8.ToArray());

        var searchRes = await client.GetAsync($"/api/v1/files/search?query={query}&scope=personal");
        searchRes.EnsureSuccessStatusCode();
        var result = (await searchRes.Content.ReadFromJsonAsync<FolderContentDto>(_jsonOptions))!;

        Assert.Contains(result.Files, f => f.Id == pFile.Id);
        Assert.DoesNotContain(result.Files, f => f.Id == sFile.Id);
    }

    [Fact]
    public async Task Search_ScopeShared_FindsOnlySharedFiles()
    {
        var (client, _, _) = await CreateNewUserClientAsync();

        var query = "shscope_" + Guid.NewGuid().ToString("N")[..6];
        var pFile = await UploadPersonalTestFileAsync(client, $"{query}_personal.txt", "abc"u8.ToArray());
        var sFile = await UploadSharedTestFileAsync(client, $"{query}_shared.txt", "def"u8.ToArray());

        var searchRes = await client.GetAsync($"/api/v1/files/search?query={query}&scope=shared");
        searchRes.EnsureSuccessStatusCode();
        var result = (await searchRes.Content.ReadFromJsonAsync<FolderContentDto>(_jsonOptions))!;

        Assert.DoesNotContain(result.Files, f => f.Id == pFile.Id);
        Assert.Contains(result.Files, f => f.Id == sFile.Id);
    }

    [Fact]
    public async Task Search_WithFolderIds_SearchesRecursivelyInsideSelectedFoldersOnly()
    {
        var (client, _, _) = await CreateNewUserClientAsync();

        // Hierarchy: RootFolder -> SubFolderA -> SubFolderB
        // and another UnrelatedFolder
        var root = await CreatePersonalFolderAsync(client, "Tree_Root");
        var subA = await CreatePersonalFolderAsync(client, "Tree_SubA", root.Id);
        var subB = await CreatePersonalFolderAsync(client, "Tree_SubB", subA.Id);
        var unrelated = await CreatePersonalFolderAsync(client, "Tree_Unrelated");

        var token = "recursivesearch_" + Guid.NewGuid().ToString("N")[..6];
        var fileInRoot = await UploadPersonalTestFileAsync(client, $"{token}_root.txt", "1"u8.ToArray(), root.Id);
        var fileInSubA = await UploadPersonalTestFileAsync(client, $"{token}_subA.txt", "2"u8.ToArray(), subA.Id);
        var fileInSubB = await UploadPersonalTestFileAsync(client, $"{token}_subB.txt", "3"u8.ToArray(), subB.Id);
        var fileInUnrelated = await UploadPersonalTestFileAsync(client, $"{token}_unrelated.txt", "4"u8.ToArray(), unrelated.Id);

        // Search scoping to SubA only (should find subA and subB files, but not root or unrelated)
        var searchRes = await client.GetAsync($"/api/v1/files/search?query={token}&folderIds={subA.Id}");
        searchRes.EnsureSuccessStatusCode();
        var result = (await searchRes.Content.ReadFromJsonAsync<FolderContentDto>(_jsonOptions))!;

        Assert.Contains(result.Files, f => f.Id == fileInSubA.Id);
        Assert.Contains(result.Files, f => f.Id == fileInSubB.Id);
        Assert.DoesNotContain(result.Files, f => f.Id == fileInRoot.Id);
        Assert.DoesNotContain(result.Files, f => f.Id == fileInUnrelated.Id);
    }

    [Fact]
    public async Task BulkMove_FolderIntoItself_ReturnsBadRequest()
    {
        var (client, _, _) = await CreateNewUserClientAsync();
        var folder = await CreatePersonalFolderAsync(client, "SelfFolder");

        var moveRes = await client.PostAsJsonAsync("/api/v1/files/bulk-move", new BulkMoveRequest
        {
            FolderIds = new List<Guid> { folder.Id },
            TargetFolderId = folder.Id
        });

        Assert.Equal(HttpStatusCode.BadRequest, moveRes.StatusCode);
    }

    [Fact]
    public async Task BulkMove_FolderIntoOwnSubfolder_ReturnsBadRequest()
    {
        var (client, _, _) = await CreateNewUserClientAsync();
        var parent = await CreatePersonalFolderAsync(client, "ParentFolder");
        var child = await CreatePersonalFolderAsync(client, "ChildFolder", parent.Id);

        var moveRes = await client.PostAsJsonAsync("/api/v1/files/bulk-move", new BulkMoveRequest
        {
            FolderIds = new List<Guid> { parent.Id },
            TargetFolderId = child.Id
        });

        Assert.Equal(HttpStatusCode.BadRequest, moveRes.StatusCode);
    }

    [Fact]
    public async Task Search_CaseInsensitiveQuery_MatchesCorrectly()
    {
        var (client, _, _) = await CreateNewUserClientAsync();
        var token = "MIXED_Case_" + Guid.NewGuid().ToString("N")[..6];
        var file = await UploadPersonalTestFileAsync(client, $"{token}_report.PDF", "data"u8.ToArray());

        // Search with all-lowercase query
        var searchResLower = await client.GetAsync($"/api/v1/files/search?query={token.ToLowerInvariant()}&scope=personal");
        searchResLower.EnsureSuccessStatusCode();
        var resultLower = (await searchResLower.Content.ReadFromJsonAsync<FolderContentDto>(_jsonOptions))!;
        Assert.Contains(resultLower.Files, f => f.Id == file.Id);

        // Search with all-uppercase query
        var searchResUpper = await client.GetAsync($"/api/v1/files/search?query={token.ToUpperInvariant()}&scope=personal");
        searchResUpper.EnsureSuccessStatusCode();
        var resultUpper = (await searchResUpper.Content.ReadFromJsonAsync<FolderContentDto>(_jsonOptions))!;
        Assert.Contains(resultUpper.Files, f => f.Id == file.Id);
    }
}
