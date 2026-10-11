using System.IO.Compression;
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

public class Release11Stage3Tests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Release11Stage3Tests(CustomWebApplicationFactory factory)
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

    [Fact]
    public async Task DownloadZip_MultipleFiles_ReturnsValidZipArchiveWithAllContents()
    {
        var (client, _) = await CreateNewUserClientAsync();

        var file1 = await UploadTestFileAsync(client, "alpha.txt", "Alpha content"u8.ToArray());
        var file2 = await UploadTestFileAsync(client, "beta.txt", "Beta content"u8.ToArray());

        var req = new DownloadZipRequest
        {
            FileIds = new List<Guid> { file1.Id, file2.Id }
        };

        var response = await client.PostAsJsonAsync("/api/v1/files/download-zip", req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);

        var zipBytes = await response.Content.ReadAsByteArrayAsync();
        using var zipStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        Assert.Equal(2, archive.Entries.Count);
        var alphaEntry = archive.GetEntry("alpha.txt");
        var betaEntry = archive.GetEntry("beta.txt");

        Assert.NotNull(alphaEntry);
        Assert.NotNull(betaEntry);

        using (var reader = new StreamReader(alphaEntry!.Open()))
        {
            Assert.Equal("Alpha content", await reader.ReadToEndAsync());
        }

        using (var reader = new StreamReader(betaEntry!.Open()))
        {
            Assert.Equal("Beta content", await reader.ReadToEndAsync());
        }
    }

    [Fact]
    public async Task DownloadZip_WithFolderHierarchy_PreservesDirectoryPaths()
    {
        var (client, _) = await CreateNewUserClientAsync();

        var rootFolderRes = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest { Name = "Projects" });
        rootFolderRes.EnsureSuccessStatusCode();
        var rootFolder = (await rootFolderRes.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions))!;

        var subFolderRes = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest { Name = "Docs", ParentFolderId = rootFolder.Id });
        subFolderRes.EnsureSuccessStatusCode();
        var subFolder = (await subFolderRes.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions))!;

        var rootFile = await UploadTestFileAsync(client, "readme.md", "# Readme"u8.ToArray(), rootFolder.Id);
        var subFile = await UploadTestFileAsync(client, "spec.txt", "specification"u8.ToArray(), subFolder.Id);

        var req = new DownloadZipRequest
        {
            FolderIds = new List<Guid> { rootFolder.Id }
        };

        var response = await client.PostAsJsonAsync("/api/v1/files/download-zip", req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var zipBytes = await response.Content.ReadAsByteArrayAsync();
        using var zipStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        var readmeEntry = archive.GetEntry("Projects/readme.md");
        var specEntry = archive.GetEntry("Projects/Docs/spec.txt");

        Assert.NotNull(readmeEntry);
        Assert.NotNull(specEntry);

        using (var reader = new StreamReader(readmeEntry!.Open()))
        {
            Assert.Equal("# Readme", await reader.ReadToEndAsync());
        }

        using (var reader = new StreamReader(specEntry!.Open()))
        {
            Assert.Equal("specification", await reader.ReadToEndAsync());
        }
    }

    [Fact]
    public async Task BulkDelete_FilesAndFolders_SoftDeletesItemsAndCascadesToNestedFiles()
    {
        var (client, login) = await CreateNewUserClientAsync();

        var folderRes = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest { Name = "DeleteFolder" });
        folderRes.EnsureSuccessStatusCode();
        var folder = (await folderRes.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions))!;

        var fileRoot = await UploadTestFileAsync(client, "root.txt", "root"u8.ToArray());
        var fileNested = await UploadTestFileAsync(client, "nested.txt", "nested"u8.ToArray(), folder.Id);

        var bulkDel = new BulkDeleteRequest
        {
            FileIds = new List<Guid> { fileRoot.Id },
            FolderIds = new List<Guid> { folder.Id }
        };

        var delRes = await client.PostAsJsonAsync("/api/v1/files/bulk-delete", bulkDel);
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        // Verify in DB that root file, folder, and nested file are all marked IsDeleted
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var dbRootFile = await db.FileItems.FindAsync(fileRoot.Id);
        var dbNestedFile = await db.FileItems.FindAsync(fileNested.Id);
        var dbFolder = await db.Folders.FindAsync(folder.Id);

        Assert.True(dbRootFile!.IsDeleted);
        Assert.True(dbNestedFile!.IsDeleted);
        Assert.True(dbFolder!.IsDeleted);
    }

    [Fact]
    public async Task BulkMove_FilesAndFolders_MovesItemsToDestinationFolder()
    {
        var (client, _) = await CreateNewUserClientAsync();

        var sourceFolderRes = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest { Name = "SourceFolder" });
        var destFolderRes = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest { Name = "DestFolder" });
        var sourceFolder = (await sourceFolderRes.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions))!;
        var destFolder = (await destFolderRes.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions))!;

        var file = await UploadTestFileAsync(client, "move_me.txt", "move me content"u8.ToArray());

        var bulkMove = new BulkMoveRequest
        {
            FileIds = new List<Guid> { file.Id },
            FolderIds = new List<Guid> { sourceFolder.Id },
            TargetFolderId = destFolder.Id
        };

        var moveRes = await client.PostAsJsonAsync("/api/v1/files/bulk-move", bulkMove);
        Assert.Equal(HttpStatusCode.NoContent, moveRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var movedFile = await db.FileItems.FindAsync(file.Id);
        var movedFolder = await db.Folders.FindAsync(sourceFolder.Id);

        Assert.Equal(destFolder.Id, movedFile!.FolderId);
        Assert.Equal(destFolder.Id, movedFolder!.ParentFolderId);
    }
}
