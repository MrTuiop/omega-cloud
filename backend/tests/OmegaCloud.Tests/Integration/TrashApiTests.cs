using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OmegaCloud.Application.DTOs.Auth;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Application.DTOs.Folders;
using OmegaCloud.Application.DTOs.Trash;

namespace OmegaCloud.Tests.Integration;

public class TrashApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public TrashApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateClientForUserAsync(string username, string password)
    {
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            UsernameOrEmail = username,
            Password = password,
            DeviceName = "Trash-Api-Test"
        });
        loginResponse.EnsureSuccessStatusCode();

        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult!.AccessToken);
        return client;
    }

    private async Task<HttpClient> CreateRegularClientAsync()
    {
        return await CreateClientForUserAsync(CustomWebApplicationFactory.DefaultRegularUsername, CustomWebApplicationFactory.DefaultRegularPassword);
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        return await CreateClientForUserAsync(CustomWebApplicationFactory.DefaultAdminUsername, CustomWebApplicationFactory.DefaultAdminPassword);
    }

    private async Task<FileItemDto> UploadFileAsync(HttpClient client, string fileName, string content = "Trash test content", Guid? folderId = null)
    {
        using var formData = new MultipartFormDataContent();
        var fileBytes = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        fileBytes.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        formData.Add(fileBytes, "file", fileName);

        if (folderId.HasValue)
        {
            formData.Add(new StringContent(folderId.Value.ToString()), "folderId");
        }

        var response = await client.PostAsync("/api/v1/files/upload", formData);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;
    }

    private async Task<FolderDto> CreateFolderAsync(HttpClient client, string folderName, Guid? parentFolderId = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest
        {
            Name = folderName,
            ParentFolderId = parentFolderId
        });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions))!;
    }

    [Fact]
    public async Task GetTrash_WithoutAuth_ReturnsUnauthorized()
    {
        var unauthClient = _factory.CreateClient();
        var response = await unauthClient.GetAsync("/api/v1/trash");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SoftDeleteFile_ShowsInTrash_AndHoldsQuota()
    {
        var client = await CreateRegularClientAsync();
        var fileName = "soft_delete_" + Guid.NewGuid().ToString("N")[..8] + ".txt";
        var content = "Content to test soft delete";
        var file = await UploadFileAsync(client, fileName, content);

        // Get quota before deletion
        var quotaBefore = await client.GetFromJsonAsync<UserQuotaDto>("/api/v1/upload/quota", _jsonOptions);

        // Soft delete file
        var delResponse = await client.DeleteAsync($"/api/v1/files/{file.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delResponse.StatusCode);

        // File is no longer accessible via normal files endpoint
        var getMetaResponse = await client.GetAsync($"/api/v1/files/{file.Id}/metadata");
        Assert.Equal(HttpStatusCode.NotFound, getMetaResponse.StatusCode);

        // Quota is HELD (used bytes unchanged)
        var quotaAfter = await client.GetFromJsonAsync<UserQuotaDto>("/api/v1/upload/quota", _jsonOptions);
        Assert.Equal(quotaBefore!.UsedBytes, quotaAfter!.UsedBytes);

        // File appears in Trash
        var trashResponse = await client.GetAsync("/api/v1/trash");
        Assert.Equal(HttpStatusCode.OK, trashResponse.StatusCode);
        var trash = await trashResponse.Content.ReadFromJsonAsync<TrashContentDto>(_jsonOptions);
        Assert.NotNull(trash);
        Assert.Contains(trash.Files, f => f.Id == file.Id);
    }

    [Fact]
    public async Task RestoreFile_RestoresToOriginalLocation_AndBecomesAccessible()
    {
        var client = await CreateRegularClientAsync();
        var folder = await CreateFolderAsync(client, "RestoreFolder_" + Guid.NewGuid().ToString("N")[..6]);
        var file = await UploadFileAsync(client, "restore_me.txt", "restore content", folder.Id);

        // Soft delete
        await client.DeleteAsync($"/api/v1/files/{file.Id}");

        // Restore file
        var restoreResponse = await client.PostAsync($"/api/v1/trash/restore/{file.Id}", null);
        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
        var restoreResult = await restoreResponse.Content.ReadFromJsonAsync<RestoreItemResultDto>(_jsonOptions);
        Assert.NotNull(restoreResult);
        Assert.Equal("File", restoreResult.ItemType);
        Assert.Equal(folder.Id, restoreResult.RestoredToFolderId);

        // File is accessible again
        var metaResponse = await client.GetAsync($"/api/v1/files/{file.Id}/metadata");
        Assert.Equal(HttpStatusCode.OK, metaResponse.StatusCode);

        // File is no longer in Trash
        var trash = await client.GetFromJsonAsync<TrashContentDto>("/api/v1/trash", _jsonOptions);
        Assert.DoesNotContain(trash!.Files, f => f.Id == file.Id);
    }

    [Fact]
    public async Task RestoreFile_ConflictWithActiveFile_ReturnsBadRequest()
    {
        var client = await CreateRegularClientAsync();
        var sharedName = "conflict_" + Guid.NewGuid().ToString("N")[..6] + ".txt";

        var file1 = await UploadFileAsync(client, sharedName, "first version");
        await client.DeleteAsync($"/api/v1/files/{file1.Id}");

        // Upload a new file with the exact same name while old is in Trash
        var file2 = await UploadFileAsync(client, sharedName, "second version");

        // Try restoring the first file -> should conflict
        var restoreResponse = await client.PostAsync($"/api/v1/trash/restore/{file1.Id}", null);
        Assert.Equal(HttpStatusCode.BadRequest, restoreResponse.StatusCode);
    }

    [Fact]
    public async Task SoftDeleteFolder_And_RestoreFolder_CascadesEntireHierarchy()
    {
        var client = await CreateRegularClientAsync();
        var rootFolder = await CreateFolderAsync(client, "RootTree_" + Guid.NewGuid().ToString("N")[..6]);
        var subFolder = await CreateFolderAsync(client, "SubTree_" + Guid.NewGuid().ToString("N")[..6], rootFolder.Id);
        var fileInside = await UploadFileAsync(client, "tree_file.txt", "inside subtree", subFolder.Id);

        // Soft delete root folder
        var deleteResponse = await client.DeleteAsync($"/api/v1/folders/{rootFolder.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Check trash has folder and file
        var trash = await client.GetFromJsonAsync<TrashContentDto>("/api/v1/trash", _jsonOptions);
        Assert.Contains(trash!.Folders, f => f.Id == rootFolder.Id);
        Assert.Contains(trash.Folders, f => f.Id == subFolder.Id);
        Assert.Contains(trash.Files, f => f.Id == fileInside.Id);

        // Restore root folder
        var restoreResponse = await client.PostAsync($"/api/v1/trash/restore/{rootFolder.Id}", null);
        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);

        // Both folders and file are restored
        var folderContent = await client.GetFromJsonAsync<FolderContentDto>($"/api/v1/folders/{subFolder.Id}", _jsonOptions);
        Assert.NotNull(folderContent);
        Assert.Contains(folderContent.Files, f => f.Id == fileInside.Id);
    }

    [Fact]
    public async Task PermanentlyDeleteFile_FreesQuotaAndRemovesFromTrash()
    {
        var client = await CreateRegularClientAsync();
        var content = "This file will be permanently obliterated.";
        var file = await UploadFileAsync(client, "obliterate_" + Guid.NewGuid().ToString("N")[..6] + ".txt", content);

        var quotaWithFile = await client.GetFromJsonAsync<UserQuotaDto>("/api/v1/upload/quota", _jsonOptions);

        // Move to trash
        await client.DeleteAsync($"/api/v1/files/{file.Id}");

        // Permanently delete from trash
        var permDeleteResponse = await client.DeleteAsync($"/api/v1/trash/permanent/{file.Id}");
        Assert.Equal(HttpStatusCode.NoContent, permDeleteResponse.StatusCode);

        // Quota is freed
        var quotaAfterPerm = await client.GetFromJsonAsync<UserQuotaDto>("/api/v1/upload/quota", _jsonOptions);
        Assert.True(quotaAfterPerm!.UsedBytes < quotaWithFile!.UsedBytes);

        // Not in trash
        var trash = await client.GetFromJsonAsync<TrashContentDto>("/api/v1/trash", _jsonOptions);
        Assert.DoesNotContain(trash!.Files, f => f.Id == file.Id);
    }

    [Fact]
    public async Task EmptyTrash_RemovesAllItems_AndFreesAllQuota()
    {
        var client = await CreateRegularClientAsync();
        var file1 = await UploadFileAsync(client, "empty1_" + Guid.NewGuid().ToString("N")[..6] + ".txt", "data 1");
        var file2 = await UploadFileAsync(client, "empty2_" + Guid.NewGuid().ToString("N")[..6] + ".txt", "data 2");
        var folder = await CreateFolderAsync(client, "empty_folder_" + Guid.NewGuid().ToString("N")[..6]);

        await client.DeleteAsync($"/api/v1/files/{file1.Id}");
        await client.DeleteAsync($"/api/v1/files/{file2.Id}");
        await client.DeleteAsync($"/api/v1/folders/{folder.Id}");

        var quotaBeforeEmpty = await client.GetFromJsonAsync<UserQuotaDto>("/api/v1/upload/quota", _jsonOptions);

        // Empty trash
        var emptyResponse = await client.DeleteAsync("/api/v1/trash/empty");
        Assert.Equal(HttpStatusCode.NoContent, emptyResponse.StatusCode);

        // Trash is empty
        var trash = await client.GetFromJsonAsync<TrashContentDto>("/api/v1/trash", _jsonOptions);
        Assert.Empty(trash!.Files);
        Assert.Empty(trash.Folders);

        // Quota is freed
        var quotaAfterEmpty = await client.GetFromJsonAsync<UserQuotaDto>("/api/v1/upload/quota", _jsonOptions);
        Assert.True(quotaAfterEmpty!.UsedBytes < quotaBeforeEmpty!.UsedBytes);
    }

    [Fact]
    public async Task UserIsolation_CannotSeeOrManipulateAnotherUsersTrash()
    {
        var regularClient = await CreateRegularClientAsync();
        var adminClient = await CreateAdminClientAsync();

        var adminFile = await UploadFileAsync(adminClient, "admin_secret_" + Guid.NewGuid().ToString("N")[..6] + ".txt", "top secret");
        await adminClient.DeleteAsync($"/api/v1/files/{adminFile.Id}");

        // Regular user trash should not contain admin's deleted file
        var regularTrash = await regularClient.GetFromJsonAsync<TrashContentDto>("/api/v1/trash", _jsonOptions);
        Assert.DoesNotContain(regularTrash!.Files, f => f.Id == adminFile.Id);

        // Regular user cannot restore admin's deleted file
        var restoreResponse = await regularClient.PostAsync($"/api/v1/trash/restore/{adminFile.Id}", null);
        Assert.Equal(HttpStatusCode.NotFound, restoreResponse.StatusCode);

        // Regular user cannot permanently delete admin's file
        var permResponse = await regularClient.DeleteAsync($"/api/v1/trash/permanent/{adminFile.Id}");
        Assert.Equal(HttpStatusCode.NotFound, permResponse.StatusCode);
    }
}
