using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OmegaCloud.Application.DTOs.Auth;
using OmegaCloud.Application.DTOs.Folders;

namespace OmegaCloud.Tests.Integration;

public class FoldersApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public FoldersApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            UsernameOrEmail = CustomWebApplicationFactory.DefaultRegularUsername,
            Password = CustomWebApplicationFactory.DefaultRegularPassword,
            DeviceName = "Folders-Api-Test"
        });
        loginResponse.EnsureSuccessStatusCode();

        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult!.AccessToken);
        return client;
    }

    [Fact]
    public async Task GetFolders_WithoutToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/folders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetRootFolders_WithToken_ReturnsOkWithRootBreadcrumbs()
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/v1/folders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<FolderContentDto>(_jsonOptions);
        Assert.NotNull(content);
        Assert.Null(content.CurrentFolder);
        Assert.NotEmpty(content.Breadcrumbs);
        Assert.Equal("Root", content.Breadcrumbs[0].Name);
    }

    [Fact]
    public async Task CreateFolder_WithValidData_ReturnsCreated()
    {
        var client = await CreateAuthenticatedClientAsync();
        var folderName = "TestFolder_" + Guid.NewGuid().ToString("N")[..8];

        var response = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest
        {
            Name = folderName,
            Color = "blue"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var folder = await response.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions);
        Assert.NotNull(folder);
        Assert.Equal(folderName, folder.Name);
        Assert.Equal("blue", folder.Color);
        Assert.Null(folder.ParentFolderId);
    }

    [Fact]
    public async Task CreateFolder_DuplicateName_ReturnsBadRequest()
    {
        var client = await CreateAuthenticatedClientAsync();
        var folderName = "DupFolder_" + Guid.NewGuid().ToString("N")[..8];

        var res1 = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest { Name = folderName });
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        var res2 = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest { Name = folderName });
        Assert.Equal(HttpStatusCode.BadRequest, res2.StatusCode);
    }

    [Fact]
    public async Task CreateFolder_InvalidName_ReturnsBadRequest()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest { Name = "folder/with/slashes" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetSubFolder_WithValidId_ReturnsFolderAndBreadcrumbs()
    {
        var client = await CreateAuthenticatedClientAsync();
        var parentName = "Parent_" + Guid.NewGuid().ToString("N")[..8];
        var childName = "Child_" + Guid.NewGuid().ToString("N")[..8];

        var parentRes = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest { Name = parentName });
        var parent = await parentRes.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions);

        var childRes = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest
        {
            Name = childName,
            ParentFolderId = parent!.Id
        });
        var child = await childRes.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions);

        var response = await client.GetAsync($"/api/v1/folders/{child!.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadFromJsonAsync<FolderContentDto>(_jsonOptions);
        Assert.NotNull(content);
        Assert.Equal(child.Id, content.CurrentFolder?.Id);
        Assert.Equal(3, content.Breadcrumbs.Count);
        Assert.Equal("Root", content.Breadcrumbs[0].Name);
        Assert.Equal(parentName, content.Breadcrumbs[1].Name);
        Assert.Equal(childName, content.Breadcrumbs[2].Name);
    }

    [Fact]
    public async Task GetFolder_NonExistent_ReturnsNotFound()
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync($"/api/v1/folders/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateFolder_RenameAndColor_ReturnsOk()
    {
        var client = await CreateAuthenticatedClientAsync();
        var initialName = "BeforeRename_" + Guid.NewGuid().ToString("N")[..8];

        var createRes = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest { Name = initialName });
        var folder = await createRes.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions);

        var patchRes = await client.PatchAsJsonAsync($"/api/v1/folders/{folder!.Id}", new UpdateFolderRequest
        {
            Name = "AfterRename_" + Guid.NewGuid().ToString("N")[..8],
            Color = "red"
        });

        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);
        var updated = await patchRes.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions);
        Assert.NotNull(updated);
        Assert.StartsWith("AfterRename_", updated.Name);
        Assert.Equal("red", updated.Color);
    }

    [Fact]
    public async Task UpdateFolder_CyclicMove_ReturnsBadRequest()
    {
        var client = await CreateAuthenticatedClientAsync();

        var parentRes = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest { Name = "CycleParent_" + Guid.NewGuid().ToString("N")[..8] });
        var parent = await parentRes.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions);

        var childRes = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest
        {
            Name = "CycleChild_" + Guid.NewGuid().ToString("N")[..8],
            ParentFolderId = parent!.Id
        });
        var child = await childRes.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions);

        // Attempt to move parent inside child
        var patchRes = await client.PatchAsJsonAsync($"/api/v1/folders/{parent.Id}", new UpdateFolderRequest
        {
            ParentFolderId = child!.Id
        });

        Assert.Equal(HttpStatusCode.BadRequest, patchRes.StatusCode);
    }

    [Fact]
    public async Task DeleteFolder_ReturnsNoContent()
    {
        var client = await CreateAuthenticatedClientAsync();

        var createRes = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest { Name = "DeleteFolder_" + Guid.NewGuid().ToString("N")[..8] });
        var folder = await createRes.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions);

        var delRes = await client.DeleteAsync($"/api/v1/folders/{folder!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        // Subsequent get returns 404
        var getRes = await client.GetAsync($"/api/v1/folders/{folder.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getRes.StatusCode);
    }
}
