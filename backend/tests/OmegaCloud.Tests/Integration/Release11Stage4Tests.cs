using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OmegaCloud.Application.DTOs.Admin;
using OmegaCloud.Application.DTOs.Auth;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Application.DTOs.Folders;
using Xunit;

namespace OmegaCloud.Tests.Integration;

public class Release11Stage4Tests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Release11Stage4Tests(CustomWebApplicationFactory factory)
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

    private async Task<(HttpClient Client, LoginResponse Login)> CreateNewUserClientAsync()
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
            AllocatedQuotaBytes = 5L * 1024 * 1024 * 1024
        });
        createRes.EnsureSuccessStatusCode();

        return await CreateClientForUserAsync(username, password, "Device-" + username);
    }

    private async Task<FileItemDto> UploadTestFileAsync(HttpClient client, string fileName, byte[] content, Guid? folderId = null)
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

    private async Task<FolderDto> CreateFolderAsync(HttpClient client, string name, Guid? parentId = null)
    {
        var res = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest
        {
            Name = name,
            ParentFolderId = parentId,
            Color = "#0071e3"
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions))!;
    }

    [Fact]
    public async Task GetRecentFiles_ReturnsRecentFiles_OrderedByDate()
    {
        var (client, _) = await CreateNewUserClientAsync();

        var f1 = await UploadTestFileAsync(client, "doc1.txt", "content 1"u8.ToArray());
        var f2 = await UploadTestFileAsync(client, "doc2.txt", "content 2"u8.ToArray());
        var f3 = await UploadTestFileAsync(client, "doc3.txt", "content 3"u8.ToArray());

        var res = await client.GetAsync("/api/v1/files/recent?limit=2");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var recent = await res.Content.ReadFromJsonAsync<List<FileItemDto>>(_jsonOptions);
        Assert.NotNull(recent);
        Assert.Equal(2, recent!.Count);
        Assert.Equal(f3.Id, recent[0].Id);
        Assert.Equal(f2.Id, recent[1].Id);
    }

    [Fact]
    public async Task SearchFiles_ScopeEverywhere_FindsFilesAcrossFolders()
    {
        var (client, _) = await CreateNewUserClientAsync();

        var folderA = await CreateFolderAsync(client, "FolderA");
        var folderB = await CreateFolderAsync(client, "FolderB", folderA.Id);

        await UploadTestFileAsync(client, "financial_report.pdf", "report"u8.ToArray());
        await UploadTestFileAsync(client, "annual_report.docx", "report"u8.ToArray(), folderA.Id);
        await UploadTestFileAsync(client, "deep_report.txt", "report"u8.ToArray(), folderB.Id);
        await UploadTestFileAsync(client, "unrelated_picture.png", "img"u8.ToArray());

        var res = await client.GetAsync("/api/v1/files/search?q=report&scope=everywhere");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var content = await res.Content.ReadFromJsonAsync<FolderContentDto>(_jsonOptions);
        Assert.NotNull(content);
        Assert.Equal(3, content!.Files.Count);
        Assert.Contains(content.Files, f => f.Name == "financial_report.pdf");
        Assert.Contains(content.Files, f => f.Name == "annual_report.docx");
        Assert.Contains(content.Files, f => f.Name == "deep_report.txt");
        Assert.DoesNotContain(content.Files, f => f.Name == "unrelated_picture.png");
    }

    [Fact]
    public async Task SearchFiles_ScopeCurrent_RestrictedToTargetFolderAndDescendants()
    {
        var (client, _) = await CreateNewUserClientAsync();

        var folder1 = await CreateFolderAsync(client, "Folder1");
        var subFolder1 = await CreateFolderAsync(client, "SubFolder1", folder1.Id);
        var folder2 = await CreateFolderAsync(client, "Folder2");

        await UploadTestFileAsync(client, "audit_in_root.txt", "test"u8.ToArray());
        await UploadTestFileAsync(client, "audit_in_f1.txt", "test"u8.ToArray(), folder1.Id);
        await UploadTestFileAsync(client, "audit_in_sub1.txt", "test"u8.ToArray(), subFolder1.Id);
        await UploadTestFileAsync(client, "audit_in_f2.txt", "test"u8.ToArray(), folder2.Id);

        // Search within folder1: should return audit_in_f1 and audit_in_sub1, but NOT audit_in_root or audit_in_f2
        var res = await client.GetAsync($"/api/v1/files/search?q=audit&scope=current&folderId={folder1.Id}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var content = await res.Content.ReadFromJsonAsync<FolderContentDto>(_jsonOptions);
        Assert.NotNull(content);
        Assert.Equal(2, content!.Files.Count);
        Assert.Contains(content.Files, f => f.Name == "audit_in_f1.txt");
        Assert.Contains(content.Files, f => f.Name == "audit_in_sub1.txt");
        Assert.DoesNotContain(content.Files, f => f.Name == "audit_in_root.txt");
        Assert.DoesNotContain(content.Files, f => f.Name == "audit_in_f2.txt");
    }

    [Fact]
    public async Task SearchFiles_Anonymous_ReturnsUnauthorized()
    {
        var anonClient = _factory.CreateClient();
        var res = await anonClient.GetAsync("/api/v1/files/search?q=test");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task CheckConflicts_IdentifiesExistingFileNames()
    {
        var (client, _) = await CreateNewUserClientAsync();

        await UploadTestFileAsync(client, "existing_doc.txt", "test"u8.ToArray());

        var req = new CheckFileConflictsRequest
        {
            FileNames = new List<string> { "existing_doc.txt", "brand_new_doc.txt" },
            FolderId = null,
            IsShared = false
        };

        var res = await client.PostAsJsonAsync("/api/v1/files/check-conflicts", req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var conflicts = await res.Content.ReadFromJsonAsync<List<string>>(_jsonOptions);
        Assert.NotNull(conflicts);
        Assert.Single(conflicts!);
        Assert.Contains("existing_doc.txt", conflicts);
    }
}
