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

public class Release12Stage2Tests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Release12Stage2Tests(CustomWebApplicationFactory factory)
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

    [Fact]
    public async Task CopyFileToShared_DeduplicatesBlob_IncrementsReferenceCount_CreatesSharedFileItem()
    {
        var (client, _, userId) = await CreateNewUserClientAsync();
        var content = "Unique content for shared copy test "u8.ToArray();
        var file = await UploadPersonalTestFileAsync(client, "personal_doc.txt", content);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var originalBlob = await db.StorageBlobs.FindAsync(file.StorageBlobId);
            Assert.NotNull(originalBlob);
            Assert.Equal(1, originalBlob!.ReferenceCount);
        }

        var copyRequest = new CopyToSharedRequest
        {
            FileIds = new List<Guid> { file.Id }
        };

        var response = await client.PostAsJsonAsync("/api/v1/files/copy-to-shared", copyRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Verify a new shared file item exists
            var sharedFile = await db.FileItems
                .FirstOrDefaultAsync(f => f.IsShared && f.StorageBlobId == file.StorageBlobId && f.Name == "personal_doc.txt");
            Assert.NotNull(sharedFile);
            Assert.True(sharedFile!.IsShared);
            Assert.Null(sharedFile.FolderId);
            Assert.NotEqual(file.Id, sharedFile.Id);

            // Verify original file item is unchanged
            var personalFile = await db.FileItems.FindAsync(file.Id);
            Assert.NotNull(personalFile);
            Assert.False(personalFile!.IsShared);

            // Verify blob reference count was incremented
            var blob = await db.StorageBlobs.FindAsync(file.StorageBlobId);
            Assert.NotNull(blob);
            Assert.Equal(2, blob!.ReferenceCount);

            // Verify no duplicate storage blobs were created
            var blobsWithHash = await db.StorageBlobs.Where(b => b.Sha256Hash == blob.Sha256Hash).CountAsync();
            Assert.Equal(1, blobsWithHash);
        }
    }

    [Fact]
    public async Task CopyFileToShared_NameCollision_AppendsSuffix()
    {
        var (client, _, _) = await CreateNewUserClientAsync();

        // 1. Existing shared file named "duplicate.txt"
        await UploadSharedTestFileAsync(client, "duplicate.txt", "Initial shared content"u8.ToArray());

        // 2. Personal file also named "duplicate.txt"
        var personalFile = await UploadPersonalTestFileAsync(client, "duplicate.txt", "My personal content"u8.ToArray());

        // 3. Copy personal file to shared root
        var copyRequest = new CopyToSharedRequest
        {
            FileIds = new List<Guid> { personalFile.Id }
        };

        var response = await client.PostAsJsonAsync("/api/v1/files/copy-to-shared", copyRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sharedCollisionCopy = await db.FileItems
            .FirstOrDefaultAsync(f => f.IsShared && f.StorageBlobId == personalFile.StorageBlobId && !f.IsDeleted);
        Assert.NotNull(sharedCollisionCopy);
        Assert.Equal("duplicate (1).txt", sharedCollisionCopy!.Name);
    }

    [Fact]
    public async Task SaveFromSharedToPersonal_DeduplicatesBlob_IncrementsReferenceCount_UpdatesUserQuota()
    {
        var (adminClient, _) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultAdminUsername,
            CustomWebApplicationFactory.DefaultAdminPassword,
            "Admin-Shared-" + Guid.NewGuid().ToString("N")[..4]);

        var (client, _, userId) = await CreateNewUserClientAsync();
        var fileBytes = new byte[2048];
        Array.Fill(fileBytes, (byte)0x42);

        var sharedFile = await UploadSharedTestFileAsync(adminClient, "announcement.bin", fileBytes);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FindAsync(userId);
            Assert.Equal(0L, user!.UsedQuotaBytes);
        }

        var saveRequest = new SaveToPersonalRequest
        {
            FileIds = new List<Guid> { sharedFile.Id }
        };

        var response = await client.PostAsJsonAsync("/api/v1/shared/save-to-personal", saveRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Verify new personal file exists
            var personalFile = await db.FileItems
                .FirstOrDefaultAsync(f => f.UserId == userId && !f.IsShared && f.StorageBlobId == sharedFile.StorageBlobId);
            Assert.NotNull(personalFile);
            Assert.False(personalFile!.IsShared);
            Assert.Equal("announcement.bin", personalFile.Name);

            // Verify blob ref count incremented
            var blob = await db.StorageBlobs.FindAsync(sharedFile.StorageBlobId);
            Assert.NotNull(blob);
            Assert.Equal(2, blob!.ReferenceCount);

            // Verify user quota updated
            var user = await db.Users.FindAsync(userId);
            Assert.Equal(2048L, user!.UsedQuotaBytes);
        }
    }

    [Fact]
    public async Task SaveFromSharedToPersonal_ExceedsUserQuota_Returns400BadRequest()
    {
        var (adminClient, _) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultAdminUsername,
            CustomWebApplicationFactory.DefaultAdminPassword,
            "Admin-Shared-" + Guid.NewGuid().ToString("N")[..4]);

        // User with strictly 1000 bytes quota
        var (client, _, userId) = await CreateNewUserClientAsync(allocatedQuota: 1000L);

        // Shared file is 2500 bytes (exceeds 1000 bytes limit)
        var largeContent = new byte[2500];
        var sharedFile = await UploadSharedTestFileAsync(adminClient, "big_video.dat", largeContent);

        var saveRequest = new SaveToPersonalRequest
        {
            FileIds = new List<Guid> { sharedFile.Id }
        };

        var response = await client.PostAsJsonAsync("/api/v1/shared/save-to-personal", saveRequest);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Verify no personal file created for this user
        var savedFile = await db.FileItems.FirstOrDefaultAsync(f => f.UserId == userId && !f.IsShared);
        Assert.Null(savedFile);

        // Verify user used quota remained 0
        var user = await db.Users.FindAsync(userId);
        Assert.Equal(0L, user!.UsedQuotaBytes);
    }

    [Fact]
    public async Task RecursiveCopyFolder_FromPersonalToShared_CopiesWholeTree()
    {
        var (client, _, userId) = await CreateNewUserClientAsync();

        // Build folder tree:
        // Personal Root
        //   └── PersonalRootTree
        //         ├── ChildSubFolder
        //         │     └── deep.txt
        //         └── shallow.txt
        var parentFolder = await CreatePersonalFolderAsync(client, "PersonalRootTree");
        var childFolder = await CreatePersonalFolderAsync(client, "ChildSubFolder", parentFolder.Id);

        var shallowFile = await UploadPersonalTestFileAsync(client, "shallow.txt", "shallow content"u8.ToArray(), parentFolder.Id);
        var deepFile = await UploadPersonalTestFileAsync(client, "deep.txt", "deep content"u8.ToArray(), childFolder.Id);

        var copyRequest = new CopyToSharedRequest
        {
            FolderIds = new List<Guid> { parentFolder.Id }
        };

        var response = await client.PostAsJsonAsync("/api/v1/files/copy-to-shared", copyRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Find shared parent folder
        var sharedParent = await db.Folders.FirstOrDefaultAsync(f => f.IsShared && f.Name == "PersonalRootTree" && !f.IsDeleted);
        Assert.NotNull(sharedParent);
        Assert.Null(sharedParent!.ParentFolderId);

        // Find shared child folder
        var sharedChild = await db.Folders.FirstOrDefaultAsync(f => f.IsShared && f.ParentFolderId == sharedParent.Id && f.Name == "ChildSubFolder" && !f.IsDeleted);
        Assert.NotNull(sharedChild);

        // Find shared shallow file
        var sharedShallow = await db.FileItems.FirstOrDefaultAsync(f => f.IsShared && f.FolderId == sharedParent.Id && f.Name == "shallow.txt");
        Assert.NotNull(sharedShallow);
        Assert.Equal(shallowFile.StorageBlobId, sharedShallow!.StorageBlobId);

        // Find shared deep file
        var sharedDeep = await db.FileItems.FirstOrDefaultAsync(f => f.IsShared && f.FolderId == sharedChild!.Id && f.Name == "deep.txt");
        Assert.NotNull(sharedDeep);
        Assert.Equal(deepFile.StorageBlobId, sharedDeep!.StorageBlobId);

        // Verify both storage blobs had their reference counts incremented
        var shallowBlob = await db.StorageBlobs.FindAsync(shallowFile.StorageBlobId);
        var deepBlob = await db.StorageBlobs.FindAsync(deepFile.StorageBlobId);

        Assert.Equal(2, shallowBlob!.ReferenceCount);
        Assert.Equal(2, deepBlob!.ReferenceCount);
    }

    [Fact]
    public async Task RecursiveSaveSharedFolder_ToPersonal_CopiesTreeAndAccountsQuota()
    {
        var (adminClient, _) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultAdminUsername,
            CustomWebApplicationFactory.DefaultAdminPassword,
            "Admin-Shared-" + Guid.NewGuid().ToString("N")[..4]);

        var (client, _, userId) = await CreateNewUserClientAsync();

        // Build shared folder tree:
        // Shared Space
        //   └── SharedProjects
        //         ├── nested.txt (300 bytes)
        var sharedFolder = await CreateSharedFolderAsync(adminClient, "SharedProjects");
        var nestedFile = await UploadSharedTestFileAsync(adminClient, "nested.txt", new byte[300], sharedFolder.Id);

        // Personal destination folder
        var personalDestFolder = await CreatePersonalFolderAsync(client, "MyDestination");

        var saveRequest = new SaveToPersonalRequest
        {
            FolderIds = new List<Guid> { sharedFolder.Id },
            TargetFolderId = personalDestFolder.Id
        };

        var response = await client.PostAsJsonAsync("/api/v1/shared/save-to-personal", saveRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Verify copied folder exists under personalDestFolder
        var copiedFolder = await db.Folders.FirstOrDefaultAsync(f => f.UserId == userId && !f.IsShared && f.ParentFolderId == personalDestFolder.Id && f.Name == "SharedProjects");
        Assert.NotNull(copiedFolder);

        // Verify copied nested file exists
        var copiedFile = await db.FileItems.FirstOrDefaultAsync(f => f.UserId == userId && !f.IsShared && f.FolderId == copiedFolder!.Id && f.Name == "nested.txt");
        Assert.NotNull(copiedFile);
        Assert.Equal(nestedFile.StorageBlobId, copiedFile!.StorageBlobId);

        // Verify user quota increased by 300 bytes
        var user = await db.Users.FindAsync(userId);
        Assert.Equal(300L, user!.UsedQuotaBytes);

        // Verify blob ref count is 2
        var blob = await db.StorageBlobs.FindAsync(nestedFile.StorageBlobId);
        Assert.Equal(2, blob!.ReferenceCount);
    }


    [Fact]
    public async Task CopyToShared_NonExistentDestinationFolder_Returns404NotFound()
    {
        var (client, _, _) = await CreateNewUserClientAsync();
        var file = await UploadPersonalTestFileAsync(client, "doc.txt", "abc"u8.ToArray());

        var req = new CopyToSharedRequest
        {
            FileIds = new List<Guid> { file.Id },
            TargetSharedFolderId = Guid.NewGuid()
        };

        var res = await client.PostAsJsonAsync("/api/v1/files/copy-to-shared", req);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task SaveToPersonal_NonExistentDestinationFolder_Returns404NotFound()
    {
        var (client, _, _) = await CreateNewUserClientAsync();
        var file = await UploadSharedTestFileAsync(client, "shared.txt", "abc"u8.ToArray());

        var req = new SaveToPersonalRequest
        {
            FileIds = new List<Guid> { file.Id },
            TargetFolderId = Guid.NewGuid()
        };

        var res = await client.PostAsJsonAsync("/api/v1/shared/save-to-personal", req);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task SaveFromSharedToPersonal_WithFolderAndNestedFileInSelection_DoesNotDuplicateOrDoubleCountQuota()
    {
        var (adminClient, _) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultAdminUsername,
            CustomWebApplicationFactory.DefaultAdminPassword,
            "Admin-Shared-" + Guid.NewGuid().ToString("N")[..4]);

        var (client, _, userId) = await CreateNewUserClientAsync();

        var sharedFolder = await CreateSharedFolderAsync(adminClient, "SharedBundle");
        var nestedFile = await UploadSharedTestFileAsync(adminClient, "inside.txt", new byte[500], sharedFolder.Id);

        // Intentionally provide BOTH sharedFolder and nestedFile
        var saveRequest = new SaveToPersonalRequest
        {
            FolderIds = new List<Guid> { sharedFolder.Id },
            FileIds = new List<Guid> { nestedFile.Id }
        };

        var response = await client.PostAsJsonAsync("/api/v1/shared/save-to-personal", saveRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. Verify file was copied inside folder
        var copiedFolder = await db.Folders.FirstOrDefaultAsync(f => f.UserId == userId && !f.IsShared && f.Name == "SharedBundle");
        Assert.NotNull(copiedFolder);

        var fileInsideFolder = await db.FileItems.FirstOrDefaultAsync(f => f.UserId == userId && !f.IsShared && f.FolderId == copiedFolder!.Id && f.Name == "inside.txt");
        Assert.NotNull(fileInsideFolder);

        // 2. Verify file was NOT duplicated at root
        var fileAtRoot = await db.FileItems.FirstOrDefaultAsync(f => f.UserId == userId && !f.IsShared && f.FolderId == null && f.Name == "inside.txt");
        Assert.Null(fileAtRoot);

        // 3. Verify user quota increased strictly by 500 bytes (NOT 1000 bytes)
        var user = await db.Users.FindAsync(userId);
        Assert.Equal(500L, user!.UsedQuotaBytes);

        // 4. Verify blob ref count is 2 (original shared + 1 personal copy, NOT 3)
        var blob = await db.StorageBlobs.FindAsync(nestedFile.StorageBlobId);
        Assert.Equal(2, blob!.ReferenceCount);
    }

    [Fact]
    public async Task CopyFolderToShared_WithFolderAndNestedFileInSelection_DoesNotDuplicateInRoot()
    {
        var (client, _, userId) = await CreateNewUserClientAsync();

        var personalFolder = await CreatePersonalFolderAsync(client, "PersonalBundle");
        var nestedFile = await UploadPersonalTestFileAsync(client, "inner_doc.txt", new byte[400], personalFolder.Id);

        // Intentionally provide BOTH personalFolder and nestedFile
        var copyRequest = new CopyToSharedRequest
        {
            FolderIds = new List<Guid> { personalFolder.Id },
            FileIds = new List<Guid> { nestedFile.Id }
        };

        var response = await client.PostAsJsonAsync("/api/v1/files/copy-to-shared", copyRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. Shared folder exists
        var sharedFolder = await db.Folders.FirstOrDefaultAsync(f => f.IsShared && f.Name == "PersonalBundle" && !f.IsDeleted);
        Assert.NotNull(sharedFolder);

        // 2. Shared file inside folder exists
        var fileInSharedFolder = await db.FileItems.FirstOrDefaultAsync(f => f.IsShared && f.FolderId == sharedFolder!.Id && f.Name == "inner_doc.txt");
        Assert.NotNull(fileInSharedFolder);

        // 3. Shared file was NOT duplicated at root
        var fileAtSharedRoot = await db.FileItems.FirstOrDefaultAsync(f => f.IsShared && f.FolderId == null && f.Name == "inner_doc.txt");
        Assert.Null(fileAtSharedRoot);

        // 4. Blob ref count is 2 (NOT 3)
        var blob = await db.StorageBlobs.FindAsync(nestedFile.StorageBlobId);
        Assert.Equal(2, blob!.ReferenceCount);
    }

    [Fact]
    public async Task CopyEmptyFolder_PersonalToShared_And_SharedToPersonal_Succeeds()
    {
        var (client, _, userId) = await CreateNewUserClientAsync();

        // 1. Empty personal folder -> shared
        var emptyPersonal = await CreatePersonalFolderAsync(client, "EmptyPersonalFolder");
        var copyReq = new CopyToSharedRequest { FolderIds = new List<Guid> { emptyPersonal.Id } };
        var copyRes = await client.PostAsJsonAsync("/api/v1/files/copy-to-shared", copyReq);
        Assert.Equal(HttpStatusCode.OK, copyRes.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sharedCopy = await db.Folders.FirstOrDefaultAsync(f => f.IsShared && f.Name == "EmptyPersonalFolder" && !f.IsDeleted);
            Assert.NotNull(sharedCopy);
        }

        // 2. Empty shared folder -> personal
        var (adminClient, _) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultAdminUsername,
            CustomWebApplicationFactory.DefaultAdminPassword,
            "Admin-Setup-" + Guid.NewGuid().ToString("N")[..4]);
        var emptyShared = await CreateSharedFolderAsync(adminClient, "EmptySharedFolder");

        var saveReq = new SaveToPersonalRequest { FolderIds = new List<Guid> { emptyShared.Id } };
        var saveRes = await client.PostAsJsonAsync("/api/v1/shared/save-to-personal", saveReq);
        Assert.Equal(HttpStatusCode.OK, saveRes.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var personalCopy = await db.Folders.FirstOrDefaultAsync(f => f.UserId == userId && !f.IsShared && f.Name == "EmptySharedFolder" && !f.IsDeleted);
            Assert.NotNull(personalCopy);

            var user = await db.Users.FindAsync(userId);
            Assert.Equal(0L, user!.UsedQuotaBytes);
        }
    }

    [Fact]
    public async Task InactiveUser_CannotCopyToShared_Or_SaveToPersonal_Returns401()
    {
        var (client, _, userId) = await CreateNewUserClientAsync();

        var personalFile = await UploadPersonalTestFileAsync(client, "some_file.txt", "content"u8.ToArray());

        // Deactivate user in database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FindAsync(userId);
            user!.IsActive = false;
            await db.SaveChangesAsync();
        }

        var copyRes = await client.PostAsJsonAsync("/api/v1/files/copy-to-shared", new CopyToSharedRequest
        {
            FileIds = new List<Guid> { personalFile.Id }
        });
        Assert.Equal(HttpStatusCode.Unauthorized, copyRes.StatusCode);

        var saveRes = await client.PostAsJsonAsync("/api/v1/shared/save-to-personal", new SaveToPersonalRequest
        {
            FileIds = new List<Guid> { personalFile.Id }
        });
        Assert.Equal(HttpStatusCode.Unauthorized, saveRes.StatusCode);
    }

    [Fact]
    public async Task Dotfile_NameCollision_GeneratesProperUniqueNameWithoutLeadingSpace()
    {
        var (client, _, _) = await CreateNewUserClientAsync();

        // 1. Existing shared file named ".gitignore"
        await UploadSharedTestFileAsync(client, ".gitignore", "build/"u8.ToArray());

        // 2. Personal file also named ".gitignore"
        var personalFile = await UploadPersonalTestFileAsync(client, ".gitignore", "temp/"u8.ToArray());

        // 3. Copy personal file to shared root
        var copyRequest = new CopyToSharedRequest
        {
            FileIds = new List<Guid> { personalFile.Id }
        };

        var response = await client.PostAsJsonAsync("/api/v1/files/copy-to-shared", copyRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var collisionCopy = await db.FileItems
            .FirstOrDefaultAsync(f => f.IsShared && f.StorageBlobId == personalFile.StorageBlobId && !f.IsDeleted);
        Assert.NotNull(collisionCopy);
        Assert.Equal(".gitignore (1)", collisionCopy!.Name);
    }
}
