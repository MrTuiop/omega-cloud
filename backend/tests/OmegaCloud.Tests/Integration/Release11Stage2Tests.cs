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
using OmegaCloud.Domain.Enums;
using OmegaCloud.Infrastructure.Data;
using SkiaSharp;
using Xunit;

namespace OmegaCloud.Tests.Integration;

public class Release11Stage2Tests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Release11Stage2Tests(CustomWebApplicationFactory factory)
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

    private async Task<(HttpClient Client, LoginResponse Login)> CreateNewUserClientAsync(string role = "User", long quota = 10L * 1024 * 1024 * 1024)
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
            Role = role,
            AllocatedQuotaBytes = quota
        });
        createRes.EnsureSuccessStatusCode();

        return await CreateClientForUserAsync(username, password, "Device-" + username);
    }

    // ==========================================
    // TASK 1: SHARED SPACE API TESTS
    // ==========================================

    [Fact]
    public async Task SharedSpace_AnonymousAccess_CanListAndDownloadSharedFiles()
    {
        // 1. Authenticated user uploads a shared file
        var (userClient, _) = await CreateNewUserClientAsync();

        var fileContent = "This is public shared content for anonymous viewers."u8.ToArray();
        using var formData = new MultipartFormDataContent();
        formData.Add(new ByteArrayContent(fileContent), "file", "public_readme.txt");

        var uploadRes = await userClient.PostAsync("/api/v1/shared/upload", formData);
        Assert.Equal(HttpStatusCode.Created, uploadRes.StatusCode);
        var uploadedFile = (await uploadRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;
        Assert.True(uploadedFile.IsShared);

        // 2. Anonymous client (no token) lists shared folders
        var anonClient = _factory.CreateClient();
        var foldersRes = await anonClient.GetAsync("/api/v1/shared/folders");
        Assert.Equal(HttpStatusCode.OK, foldersRes.StatusCode);
        var folderContent = (await foldersRes.Content.ReadFromJsonAsync<FolderContentDto>(_jsonOptions))!;
        Assert.NotNull(folderContent);
        Assert.Contains(folderContent.Files, f => f.Id == uploadedFile.Id && f.Name == "public_readme.txt");

        // 3. Anonymous client gets shared metadata
        var metaRes = await anonClient.GetAsync($"/api/v1/shared/files/{uploadedFile.Id}/metadata");
        Assert.Equal(HttpStatusCode.OK, metaRes.StatusCode);
        var meta = (await metaRes.Content.ReadFromJsonAsync<FileMetadataDto>(_jsonOptions))!;
        Assert.Equal("public_readme.txt", meta.Name);
        Assert.True(meta.IsShared);

        // 4. Anonymous client downloads shared file
        var dlRes = await anonClient.GetAsync($"/api/v1/shared/files/{uploadedFile.Id}/download");
        Assert.Equal(HttpStatusCode.OK, dlRes.StatusCode);
        var downloadedBytes = await dlRes.Content.ReadAsByteArrayAsync();
        Assert.Equal(fileContent, downloadedBytes);

        // 5. Anonymous upload is rejected (401)
        using var anonUploadData = new MultipartFormDataContent();
        anonUploadData.Add(new ByteArrayContent("evil"u8.ToArray()), "file", "evil.txt");
        var anonUploadRes = await anonClient.PostAsync("/api/v1/shared/upload", anonUploadData);
        Assert.Equal(HttpStatusCode.Unauthorized, anonUploadRes.StatusCode);
    }

    [Fact]
    public async Task SharedSpace_PersonalFile_CannotBeAccessedViaSharedApi()
    {
        // User uploads a personal (non-shared) file
        var (userClient, _) = await CreateNewUserClientAsync();
        using var formData = new MultipartFormDataContent();
        formData.Add(new ByteArrayContent("Private personal secret"u8.ToArray()), "file", "private_secret.txt");

        var uploadRes = await userClient.PostAsync("/api/v1/files/upload", formData);
        uploadRes.EnsureSuccessStatusCode();
        var fileDto = (await uploadRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;
        Assert.False(fileDto.IsShared);

        // Accessing personal file via /api/v1/shared must return 404
        var anonClient = _factory.CreateClient();
        var sharedRes = await anonClient.GetAsync($"/api/v1/shared/files/{fileDto.Id}/metadata");
        Assert.Equal(HttpStatusCode.NotFound, sharedRes.StatusCode);

        var sharedDlRes = await anonClient.GetAsync($"/api/v1/shared/files/{fileDto.Id}/download");
        Assert.Equal(HttpStatusCode.NotFound, sharedDlRes.StatusCode);
    }

    [Fact]
    public async Task SharedSpace_DeletePermissions_OnlyAuthorOrAdminAllowed()
    {
        // User A creates a shared file
        var (userAClient, userALogin) = await CreateNewUserClientAsync();
        using var formA = new MultipartFormDataContent();
        formA.Add(new ByteArrayContent("User A shared file"u8.ToArray()), "file", "doc_by_user_a.txt");
        var upRes = await userAClient.PostAsync("/api/v1/shared/upload", formA);
        upRes.EnsureSuccessStatusCode();
        var sharedFile = (await upRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;

        // User B attempts to delete User A's file -> 403 Forbidden
        var (userBClient, _) = await CreateNewUserClientAsync();
        var userBDeleteRes = await userBClient.DeleteAsync($"/api/v1/shared/files/{sharedFile.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, userBDeleteRes.StatusCode);

        // Admin can delete User A's file -> 204 NoContent
        var (adminClient, _) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultAdminUsername,
            CustomWebApplicationFactory.DefaultAdminPassword,
            "Admin-Del");
        var adminDelRes = await adminClient.DeleteAsync($"/api/v1/shared/files/{sharedFile.Id}");
        Assert.Equal(HttpStatusCode.NoContent, adminDelRes.StatusCode);

        // Now test Author deleting own file:
        using var formA2 = new MultipartFormDataContent();
        formA2.Add(new ByteArrayContent("User A second file"u8.ToArray()), "file", "doc2_by_user_a.txt");
        var upRes2 = await userAClient.PostAsync("/api/v1/shared/upload", formA2);
        upRes2.EnsureSuccessStatusCode();
        var sharedFile2 = (await upRes2.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;

        var authorDelRes = await userAClient.DeleteAsync($"/api/v1/shared/files/{sharedFile2.Id}");
        Assert.Equal(HttpStatusCode.NoContent, authorDelRes.StatusCode);
    }

    [Fact]
    public async Task SharedSpace_Upload_DeductsAuthorQuota()
    {
        var (userClient, userLogin) = await CreateNewUserClientAsync();

        // Check initial quota
        var initQuotaRes = await userClient.GetAsync("/api/v1/upload/quota");
        initQuotaRes.EnsureSuccessStatusCode();
        var initQuota = (await initQuotaRes.Content.ReadFromJsonAsync<UserQuotaDto>(_jsonOptions))!;

        var uploadSizeBytes = 5000;
        var dummyData = new byte[uploadSizeBytes];
        new Random(42).NextBytes(dummyData);

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(dummyData), "file", "quota_test_shared.bin");
        var uploadRes = await userClient.PostAsync("/api/v1/shared/upload", form);
        uploadRes.EnsureSuccessStatusCode();

        // Check updated quota
        var afterQuotaRes = await userClient.GetAsync("/api/v1/upload/quota");
        afterQuotaRes.EnsureSuccessStatusCode();
        var afterQuota = (await afterQuotaRes.Content.ReadFromJsonAsync<UserQuotaDto>(_jsonOptions))!;

        Assert.Equal(initQuota.UsedBytes + uploadSizeBytes, afterQuota.UsedBytes);
    }

    // ==========================================
    // TASK 2: UPLOAD COLLISION ENGINE TESTS
    // ==========================================

    [Fact]
    public async Task Conflicts_PreflightCheck_ReturnsExistingDuplicates()
    {
        var (client, _) = await CreateNewUserClientAsync();

        // 1. Upload two files: "doc1.txt" and "image.png"
        using (var f1 = new MultipartFormDataContent())
        {
            f1.Add(new ByteArrayContent("one"u8.ToArray()), "file", "doc1.txt");
            (await client.PostAsync("/api/v1/files/upload", f1)).EnsureSuccessStatusCode();
        }
        using (var f2 = new MultipartFormDataContent())
        {
            f2.Add(new ByteArrayContent("two"u8.ToArray()), "file", "image.png");
            (await client.PostAsync("/api/v1/files/upload", f2)).EnsureSuccessStatusCode();
        }

        // 2. Check conflicts for ["doc1.txt", "notes.pdf", "image.png"]
        var checkRes = await client.PostAsJsonAsync("/api/v1/files/check-conflicts", new CheckFileConflictsRequest
        {
            FileNames = new List<string> { "doc1.txt", "notes.pdf", "image.png" }
        });
        Assert.Equal(HttpStatusCode.OK, checkRes.StatusCode);
        var duplicates = await checkRes.Content.ReadFromJsonAsync<List<string>>(_jsonOptions);
        Assert.NotNull(duplicates);
        Assert.Equal(2, duplicates.Count);
        Assert.Contains("doc1.txt", duplicates);
        Assert.Contains("image.png", duplicates);
        Assert.DoesNotContain("notes.pdf", duplicates);

        // 3. Check conflicts with details=true
        var detailRes = await client.PostAsJsonAsync("/api/v1/files/check-conflicts?details=true", new CheckFileConflictsRequest
        {
            FileNames = new List<string> { "doc1.txt" }
        });
        Assert.Equal(HttpStatusCode.OK, detailRes.StatusCode);
        var details = await detailRes.Content.ReadFromJsonAsync<List<FileConflictDetailDto>>(_jsonOptions);
        Assert.NotNull(details);
        Assert.Single(details);
        Assert.Equal("doc1.txt", details[0].FileName);
        Assert.True(details[0].SizeBytes > 0);
    }

    [Fact]
    public async Task Conflicts_StrategyRename_CreatesNumberedSuffix()
    {
        var (client, _) = await CreateNewUserClientAsync();

        // Upload original report.pdf
        using (var f1 = new MultipartFormDataContent())
        {
            f1.Add(new ByteArrayContent("original"u8.ToArray()), "file", "report.pdf");
            (await client.PostAsync("/api/v1/files/upload", f1)).EnsureSuccessStatusCode();
        }

        // Upload duplicate with ConflictResolutionStrategy.Rename
        using (var f2 = new MultipartFormDataContent())
        {
            f2.Add(new ByteArrayContent("copy1"u8.ToArray()), "file", "report.pdf");
            f2.Add(new StringContent("Rename"), "conflictResolution");
            var res2 = await client.PostAsync("/api/v1/files/upload", f2);
            Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
            var item2 = (await res2.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;
            Assert.Equal("report (1).pdf", item2.Name);
        }

        // Upload another duplicate with Rename -> report (2).pdf
        using (var f3 = new MultipartFormDataContent())
        {
            f3.Add(new ByteArrayContent("copy2"u8.ToArray()), "file", "report.pdf");
            f3.Add(new StringContent("Rename"), "conflictResolution");
            var res3 = await client.PostAsync("/api/v1/files/upload", f3);
            Assert.Equal(HttpStatusCode.Created, res3.StatusCode);
            var item3 = (await res3.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;
            Assert.Equal("report (2).pdf", item3.Name);
        }
    }

    [Fact]
    public async Task Conflicts_StrategySkip_ReturnsExistingFileWithoutDuplication()
    {
        var (client, _) = await CreateNewUserClientAsync();

        var originalContent = "Original file content"u8.ToArray();
        FileItemDto origItem;
        using (var f1 = new MultipartFormDataContent())
        {
            f1.Add(new ByteArrayContent(originalContent), "file", "skip_target.txt");
            var res1 = await client.PostAsync("/api/v1/files/upload", f1);
            res1.EnsureSuccessStatusCode();
            origItem = (await res1.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;
        }

        // Re-upload with Skip
        using (var f2 = new MultipartFormDataContent())
        {
            f2.Add(new ByteArrayContent("Attempted new content"u8.ToArray()), "file", "skip_target.txt");
            f2.Add(new StringContent("Skip"), "conflictResolution");
            var res2 = await client.PostAsync("/api/v1/files/upload", f2);
            Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
            var returnedItem = (await res2.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;
            Assert.Equal(origItem.Id, returnedItem.Id);
            Assert.Equal(origItem.SizeBytes, returnedItem.SizeBytes);
        }

        // Download to confirm content was NOT overwritten
        var dlRes = await client.GetAsync($"/api/v1/files/{origItem.Id}/download");
        var downloadedBytes = await dlRes.Content.ReadAsByteArrayAsync();
        Assert.Equal(originalContent, downloadedBytes);
    }

    [Fact]
    public async Task Conflicts_StrategyReplace_UpdatesContentAndAdjustsQuota()
    {
        var (client, _) = await CreateNewUserClientAsync();

        // 1. Upload small file (100 bytes)
        var smallBytes = new byte[100];
        Array.Fill(smallBytes, (byte)'A');
        FileItemDto initialItem;
        using (var f1 = new MultipartFormDataContent())
        {
            f1.Add(new ByteArrayContent(smallBytes), "file", "replace_me.dat");
            var res1 = await client.PostAsync("/api/v1/files/upload", f1);
            res1.EnsureSuccessStatusCode();
            initialItem = (await res1.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;
        }

        var quotaAfterSmall = (await (await client.GetAsync("/api/v1/upload/quota")).Content.ReadFromJsonAsync<UserQuotaDto>(_jsonOptions))!;

        // 2. Upload larger file (500 bytes) with ConflictResolutionStrategy.Replace
        var largerBytes = new byte[500];
        Array.Fill(largerBytes, (byte)'B');
        using (var f2 = new MultipartFormDataContent())
        {
            f2.Add(new ByteArrayContent(largerBytes), "file", "replace_me.dat");
            f2.Add(new StringContent("Replace"), "conflictResolution");
            var res2 = await client.PostAsync("/api/v1/files/upload", f2);
            Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
            var replacedItem = (await res2.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;
            Assert.Equal(initialItem.Id, replacedItem.Id);
            Assert.Equal(500, replacedItem.SizeBytes);
        }

        // 3. Verify quota increased exactly by diff (400 bytes)
        var quotaAfterReplace = (await (await client.GetAsync("/api/v1/upload/quota")).Content.ReadFromJsonAsync<UserQuotaDto>(_jsonOptions))!;
        Assert.Equal(quotaAfterSmall.UsedBytes + 400, quotaAfterReplace.UsedBytes);

        // 4. Verify downloaded content is the new content
        var dlRes = await client.GetAsync($"/api/v1/files/{initialItem.Id}/download");
        var dlBytes = await dlRes.Content.ReadAsByteArrayAsync();
        Assert.Equal(largerBytes, dlBytes);
    }

    // ==========================================
    // TASK 3: THUMBNAILS & STREAMING TESTS
    // ==========================================

    [Fact]
    public async Task Thumbnail_ImageFile_GeneratesResizedWebpAndCaches()
    {
        var (client, _) = await CreateNewUserClientAsync();

        // Generate a test 400x300 PNG image using SkiaSharp
        using var bitmap = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
            using var paint = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(50, 50, 200, 100, paint);
        }
        using var img = SKImage.FromBitmap(bitmap);
        using var pngData = img.Encode(SKEncodedImageFormat.Png, 100);
        var pngBytes = pngData.ToArray();

        // Upload the image
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(pngBytes), "file", "photo.png");
        var upRes = await client.PostAsync("/api/v1/files/upload", form);
        upRes.EnsureSuccessStatusCode();
        var fileDto = (await upRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;

        // Request thumbnail with width=100, height=100
        var thumbRes = await client.GetAsync($"/api/v1/files/{fileDto.Id}/thumbnail?width=100&height=100&format=webp");
        Assert.Equal(HttpStatusCode.OK, thumbRes.StatusCode);
        Assert.Equal("image/webp", thumbRes.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(thumbRes.Headers.ETag);

        var thumbBytes = await thumbRes.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(thumbBytes);

        // Verify the generated thumbnail can be decoded and is resized to <= 100x100
        using var decodedThumb = SKBitmap.Decode(thumbBytes);
        Assert.NotNull(decodedThumb);
        Assert.True(decodedThumb.Width <= 100);
        Assert.True(decodedThumb.Height <= 100);
    }

    [Fact]
    public async Task Thumbnail_DocumentFile_ReturnsSvgPreviewCard()
    {
        var (client, _) = await CreateNewUserClientAsync();

        // Upload a PDF/text document
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent("Sample document text content"u8.ToArray()), "file", "specification.pdf");
        var upRes = await client.PostAsync("/api/v1/files/upload", form);
        upRes.EnsureSuccessStatusCode();
        var fileDto = (await upRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;

        // Request thumbnail
        var thumbRes = await client.GetAsync($"/api/v1/files/{fileDto.Id}/thumbnail?width=256&height=256");
        Assert.Equal(HttpStatusCode.OK, thumbRes.StatusCode);
        Assert.Equal("image/svg+xml", thumbRes.Content.Headers.ContentType?.MediaType);

        var svgContent = await thumbRes.Content.ReadAsStringAsync();
        Assert.Contains("<svg", svgContent);
        Assert.Contains("PDF", svgContent);
    }

    [Fact]
    public async Task Streaming_AudioVideo_SupportsHttp206PartialContent()
    {
        var (client, _) = await CreateNewUserClientAsync();

        // Upload a simulated video file (10,000 bytes)
        var videoBytes = new byte[10000];
        for (int i = 0; i < videoBytes.Length; i++) videoBytes[i] = (byte)(i % 256);

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(videoBytes), "file", "demo_video.mp4");
        var upRes = await client.PostAsync("/api/v1/files/upload", form);
        upRes.EnsureSuccessStatusCode();
        var fileDto = (await upRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;

        // 1. Direct stream request without Range
        var streamRes = await client.GetAsync($"/api/v1/files/{fileDto.Id}/stream");
        Assert.Equal(HttpStatusCode.OK, streamRes.StatusCode);
        Assert.Equal("video/mp4", streamRes.Content.Headers.ContentType?.MediaType);
        Assert.True(streamRes.Headers.Contains("Accept-Ranges"));

        // 2. Partial content Range request: bytes=0-99 (first 100 bytes)
        var rangeReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/files/{fileDto.Id}/stream");
        rangeReq.Headers.Range = new RangeHeaderValue(0, 99);
        var rangeRes = await client.SendAsync(rangeReq);

        Assert.Equal(HttpStatusCode.PartialContent, rangeRes.StatusCode);
        var rangeContent = await rangeRes.Content.ReadAsByteArrayAsync();
        Assert.Equal(100, rangeContent.Length);
        Assert.Equal(videoBytes[0..100], rangeContent);
        Assert.Equal("bytes 0-99/10000", rangeRes.Content.Headers.ContentRange?.ToString());

        // 3. Partial content Range request: middle range 500-999 (500 bytes)
        var midRangeReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/files/{fileDto.Id}/stream");
        midRangeReq.Headers.Range = new RangeHeaderValue(500, 999);
        var midRes = await client.SendAsync(midRangeReq);

        Assert.Equal(HttpStatusCode.PartialContent, midRes.StatusCode);
        var midContent = await midRes.Content.ReadAsByteArrayAsync();
        Assert.Equal(500, midContent.Length);
        Assert.Equal(videoBytes[500..1000], midContent);
    }

    // ==========================================
    // TASK 4: USER ACTIVITY & LAST ACTIVE AT TESTS
    // ==========================================

    [Fact]
    public async Task LastActiveAt_TrackedOnActivity_AndPresentInAdminDto()
    {
        var (adminClient, _) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultAdminUsername,
            CustomWebApplicationFactory.DefaultAdminPassword,
            "Admin-Monitor");

        var (userClient, userLogin) = await CreateNewUserClientAsync();

        // 1. Make an authenticated call to trigger UserActivityMiddleware
        var meRes = await userClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, meRes.StatusCode);

        // 2. Fetch users list as Admin
        var usersRes = await adminClient.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.OK, usersRes.StatusCode);
        var users = (await usersRes.Content.ReadFromJsonAsync<List<AdminUserDto>>(_jsonOptions))!;

        var adminUserEntry = users.FirstOrDefault(u => u.Id == userLogin.User.Id);
        Assert.NotNull(adminUserEntry);
        Assert.NotNull(adminUserEntry.LastActiveAt);
        // LastActiveAt should be within the last minute
        Assert.True(adminUserEntry.LastActiveAt.Value > DateTime.UtcNow.AddMinutes(-5));
        Assert.True(adminUserEntry.LastActiveAt.Value <= DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task AdminUser_NeverLoggedIn_HasNullLastActiveAt()
    {
        var (adminClient, _) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultAdminUsername,
            CustomWebApplicationFactory.DefaultAdminPassword,
            "Admin-Check");

        var newUsername = "inactive_" + Guid.NewGuid().ToString("N")[..6];
        var createRes = await adminClient.PostAsJsonAsync("/api/v1/admin/users", new CreateUserRequest
        {
            Username = newUsername,
            Email = $"{newUsername}@test.local",
            Password = "Password123!",
            Role = "User"
        });
        createRes.EnsureSuccessStatusCode();
        var createdUser = (await createRes.Content.ReadFromJsonAsync<AdminUserDto>(_jsonOptions))!;

        var usersRes = await adminClient.GetAsync("/api/v1/admin/users");
        usersRes.EnsureSuccessStatusCode();
        var users = (await usersRes.Content.ReadFromJsonAsync<List<AdminUserDto>>(_jsonOptions))!;

        var entry = users.FirstOrDefault(u => u.Id == createdUser.Id);
        Assert.NotNull(entry);
        Assert.Null(entry.LastActiveAt);
    }

    [Fact]
    public async Task UploadSession_ConflictReplace_ReplacesBlobAndAdjustsQuota()
    {
        var (client, _) = await CreateNewUserClientAsync();

        // 1. Create initial file
        var initialData = "Initial session content"u8.ToArray();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(initialData), "file", "chunk_conflict.txt");
        var upRes = await client.PostAsync("/api/v1/files/upload", form);
        upRes.EnsureSuccessStatusCode();
        var initialFile = (await upRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;

        var quotaBefore = (await (await client.GetAsync("/api/v1/upload/quota")).Content.ReadFromJsonAsync<UserQuotaDto>(_jsonOptions))!;

        // 2. Start chunked session with ConflictResolution = Replace
        var newData = "New much longer replacement content uploaded via chunked session!"u8.ToArray();
        var initRes = await client.PostAsJsonAsync("/api/v1/upload/sessions/init", new OmegaCloud.Application.DTOs.Upload.InitUploadSessionRequest
        {
            FileName = "chunk_conflict.txt",
            TotalSizeBytes = newData.Length,
            ConflictResolution = ConflictResolutionStrategy.Replace
        });
        initRes.EnsureSuccessStatusCode();
        var session = (await initRes.Content.ReadFromJsonAsync<OmegaCloud.Application.DTOs.Upload.UploadSessionDto>(_jsonOptions))!;

        // 3. Upload chunk
        using var chunkContent = new ByteArrayContent(newData);
        var chunkRes = await client.PutAsync($"/api/v1/upload/sessions/{session.SessionId}/chunks/0", chunkContent);
        chunkRes.EnsureSuccessStatusCode();

        // 4. Complete session
        var completeRes = await client.PostAsJsonAsync($"/api/v1/upload/sessions/{session.SessionId}/complete", new OmegaCloud.Application.DTOs.Upload.CompleteUploadSessionRequest
        {
            ConflictResolution = ConflictResolutionStrategy.Replace
        });
        Assert.Equal(HttpStatusCode.Created, completeRes.StatusCode);
        var completedItem = (await completeRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;
        Assert.Equal(initialFile.Id, completedItem.Id);
        Assert.Equal(newData.Length, completedItem.SizeBytes);

        // 5. Download and verify content
        var dlRes = await client.GetAsync($"/api/v1/files/{completedItem.Id}/download");
        var downloadedBytes = await dlRes.Content.ReadAsByteArrayAsync();
        Assert.Equal(newData, downloadedBytes);

        // 6. Verify quota was adjusted by diff
        var quotaAfter = (await (await client.GetAsync("/api/v1/upload/quota")).Content.ReadFromJsonAsync<UserQuotaDto>(_jsonOptions))!;
        var diff = newData.Length - initialData.Length;
        Assert.Equal(quotaBefore.UsedBytes + diff, quotaAfter.UsedBytes);
    }

    [Fact]
    public async Task UploadSession_ConflictSkip_ReturnsExistingWithoutRewriting()
    {
        var (client, _) = await CreateNewUserClientAsync();

        var origData = "Original content before skip"u8.ToArray();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(origData), "file", "skip_chunk.txt");
        var upRes = await client.PostAsync("/api/v1/files/upload", form);
        upRes.EnsureSuccessStatusCode();
        var origFile = (await upRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;

        // Init session with Skip
        var initRes = await client.PostAsJsonAsync("/api/v1/upload/sessions/init", new OmegaCloud.Application.DTOs.Upload.InitUploadSessionRequest
        {
            FileName = "skip_chunk.txt",
            TotalSizeBytes = 500,
            ConflictResolution = ConflictResolutionStrategy.Skip
        });
        initRes.EnsureSuccessStatusCode();
        var session = (await initRes.Content.ReadFromJsonAsync<OmegaCloud.Application.DTOs.Upload.UploadSessionDto>(_jsonOptions))!;

        // Complete without uploading chunks (Skip returns existing file)
        var compRes = await client.PostAsJsonAsync($"/api/v1/upload/sessions/{session.SessionId}/complete", new OmegaCloud.Application.DTOs.Upload.CompleteUploadSessionRequest
        {
            ConflictResolution = ConflictResolutionStrategy.Skip
        });
        Assert.Equal(HttpStatusCode.Created, compRes.StatusCode);
        var returnedItem = (await compRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;
        Assert.Equal(origFile.Id, returnedItem.Id);
        Assert.Equal(origFile.SizeBytes, returnedItem.SizeBytes);
    }

    [Fact]
    public async Task UploadSession_UploadIntoSharedFolder_CreatedByAnotherUser_Succeeds()
    {
        // User A creates shared folder
        var (userAClient, _) = await CreateNewUserClientAsync();
        var folderRes = await userAClient.PostAsJsonAsync("/api/v1/shared/folders", new CreateFolderRequest
        {
            Name = "TeamFolder_" + Guid.NewGuid().ToString("N")[..6]
        });
        folderRes.EnsureSuccessStatusCode();
        var sharedFolder = (await folderRes.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions))!;
        Assert.True(sharedFolder.IsShared);

        // User B uploads into User A's shared folder via chunked session
        var (userBClient, _) = await CreateNewUserClientAsync();
        var fileData = "Chunked file in shared folder created by User A"u8.ToArray();

        var initRes = await userBClient.PostAsJsonAsync("/api/v1/upload/sessions/init", new OmegaCloud.Application.DTOs.Upload.InitUploadSessionRequest
        {
            FileName = "shared_doc.txt",
            TotalSizeBytes = fileData.Length,
            FolderId = sharedFolder.Id,
            IsShared = true
        });
        initRes.EnsureSuccessStatusCode();
        var session = (await initRes.Content.ReadFromJsonAsync<OmegaCloud.Application.DTOs.Upload.UploadSessionDto>(_jsonOptions))!;

        using var chunkContent = new ByteArrayContent(fileData);
        var chunkRes = await userBClient.PutAsync($"/api/v1/upload/sessions/{session.SessionId}/chunks/0", chunkContent);
        chunkRes.EnsureSuccessStatusCode();

        var compRes = await userBClient.PostAsJsonAsync($"/api/v1/upload/sessions/{session.SessionId}/complete", new OmegaCloud.Application.DTOs.Upload.CompleteUploadSessionRequest());
        Assert.Equal(HttpStatusCode.Created, compRes.StatusCode);
        var item = (await compRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;
        Assert.True(item.IsShared);
        Assert.Equal(sharedFolder.Id, item.FolderId);
    }

    [Fact]
    public async Task Thumbnail_CachedHit_WorksEfficientlyAndSVGValid()
    {
        var (client, _) = await CreateNewUserClientAsync();

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent("Doc content for SVG clip test"u8.ToArray()), "file", "architecture.pdf");
        var upRes = await client.PostAsync("/api/v1/files/upload", form);
        upRes.EnsureSuccessStatusCode();
        var fileDto = (await upRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;

        // 1st request generates thumbnail
        var res1 = await client.GetAsync($"/api/v1/files/{fileDto.Id}/thumbnail?width=256&height=256");
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var svg1 = await res1.Content.ReadAsStringAsync();
        Assert.Contains("<clipPath id=\"textClip\">", svg1);

        // 2nd request uses disk cache
        var res2 = await client.GetAsync($"/api/v1/files/{fileDto.Id}/thumbnail?width=256&height=256");
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        Assert.Equal(res1.Headers.ETag, res2.Headers.ETag);
    }

    [Fact]
    public async Task SharedSpace_StreamSharedFile_WorksWithHttp206()
    {
        var (client, _) = await CreateNewUserClientAsync();
        var mediaBytes = new byte[2000];
        new Random(123).NextBytes(mediaBytes);

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(mediaBytes), "file", "shared_audio.mp3");
        var upRes = await client.PostAsync("/api/v1/shared/upload", form);
        upRes.EnsureSuccessStatusCode();
        var fileDto = (await upRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;

        var anonClient = _factory.CreateClient();
        var streamReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/shared/files/{fileDto.Id}/stream");
        streamReq.Headers.Range = new RangeHeaderValue(0, 499);
        var streamRes = await anonClient.SendAsync(streamReq);

        Assert.Equal(HttpStatusCode.PartialContent, streamRes.StatusCode);
        var content = await streamRes.Content.ReadAsByteArrayAsync();
        Assert.Equal(500, content.Length);
        Assert.Equal(mediaBytes[0..500], content);
    }

    [Fact]
    public async Task SharedSpace_AdminReplacesSharedFile_RefundsAuthorAndChargesAdmin()
    {
        // 1. User A uploads shared file (100 bytes)
        var (userAClient, _) = await CreateNewUserClientAsync();
        var initial100Bytes = new byte[100];
        Array.Fill(initial100Bytes, (byte)'A');

        using (var formA = new MultipartFormDataContent())
        {
            formA.Add(new ByteArrayContent(initial100Bytes), "file", "company_policy.pdf");
            var upA = await userAClient.PostAsync("/api/v1/shared/upload", formA);
            upA.EnsureSuccessStatusCode();
        }

        var quotaAAfterUpload = (await (await userAClient.GetAsync("/api/v1/upload/quota")).Content.ReadFromJsonAsync<UserQuotaDto>(_jsonOptions))!;
        Assert.Equal(100, quotaAAfterUpload.UsedBytes);

        // 2. User B (regular user) tries to replace it -> 403 Forbidden
        var (userBClient, _) = await CreateNewUserClientAsync();
        using (var formB = new MultipartFormDataContent())
        {
            formB.Add(new ByteArrayContent(new byte[300]), "file", "company_policy.pdf");
            formB.Add(new StringContent("Replace"), "conflictResolution");
            var upB = await userBClient.PostAsync("/api/v1/shared/upload", formB);
            Assert.Equal(HttpStatusCode.Forbidden, upB.StatusCode);
        }

        // 3. Admin replaces company_policy.pdf with 300 bytes
        var (adminClient, _) = await CreateClientForUserAsync(
            CustomWebApplicationFactory.DefaultAdminUsername,
            CustomWebApplicationFactory.DefaultAdminPassword,
            "Admin-Replace-Test");

        var adminQuotaBefore = (await (await adminClient.GetAsync("/api/v1/upload/quota")).Content.ReadFromJsonAsync<UserQuotaDto>(_jsonOptions))!;

        var admin300Bytes = new byte[300];
        Array.Fill(admin300Bytes, (byte)'Z');
        using (var formAdmin = new MultipartFormDataContent())
        {
            formAdmin.Add(new ByteArrayContent(admin300Bytes), "file", "company_policy.pdf");
            formAdmin.Add(new StringContent("Replace"), "conflictResolution");
            var upAdmin = await adminClient.PostAsync("/api/v1/shared/upload", formAdmin);
            Assert.Equal(HttpStatusCode.Created, upAdmin.StatusCode);
            var replacedItem = (await upAdmin.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions))!;
            Assert.Equal(300, replacedItem.SizeBytes);
        }

        // 4. Verify User A quota was refunded (back to 0)
        var quotaAAfterRefund = (await (await userAClient.GetAsync("/api/v1/upload/quota")).Content.ReadFromJsonAsync<UserQuotaDto>(_jsonOptions))!;
        Assert.Equal(0, quotaAAfterRefund.UsedBytes);

        // 5. Verify Admin quota increased by 300
        var adminQuotaAfter = (await (await adminClient.GetAsync("/api/v1/upload/quota")).Content.ReadFromJsonAsync<UserQuotaDto>(_jsonOptions))!;
        Assert.Equal(adminQuotaBefore.UsedBytes + 300, adminQuotaAfter.UsedBytes);
    }
}
