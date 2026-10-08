using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OmegaCloud.Application.DTOs.Auth;
using OmegaCloud.Application.DTOs.Files;

namespace OmegaCloud.Tests.Integration;

public class FilesApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public FilesApiTests(CustomWebApplicationFactory factory)
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
            DeviceName = "Files-Api-Test"
        });
        loginResponse.EnsureSuccessStatusCode();

        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult!.AccessToken);
        return client;
    }

    [Fact]
    public async Task UploadFile_WithoutToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("test")), "file", "test.txt");

        var response = await client.PostAsync("/api/v1/files/upload", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UploadFile_ViaMultipart_ReturnsCreated()
    {
        var client = await CreateAuthenticatedClientAsync();
        var fileContent = "This is a test file for multipart upload.";
        var fileName = "document_" + Guid.NewGuid().ToString("N")[..8] + ".txt";

        using var content = new MultipartFormDataContent();
        var fileBytes = new ByteArrayContent(Encoding.UTF8.GetBytes(fileContent));
        fileBytes.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileBytes, "file", fileName);

        var response = await client.PostAsync("/api/v1/files/upload", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions);
        Assert.NotNull(result);
        Assert.Equal(fileName, result.Name);
        Assert.Equal("txt", result.Extension);
        Assert.Equal("text/plain", result.MimeType);
        Assert.Equal(Encoding.UTF8.GetByteCount(fileContent), result.SizeBytes);
    }

    [Fact]
    public async Task UploadFile_ViaDirectStream_ReturnsCreated()
    {
        var client = await CreateAuthenticatedClientAsync();
        var fileContent = "Direct stream endpoint verification payload.";
        var fileName = "direct_" + Guid.NewGuid().ToString("N")[..8] + ".txt";

        using var content = new MultipartFormDataContent();
        var fileBytes = new ByteArrayContent(Encoding.UTF8.GetBytes(fileContent));
        fileBytes.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileBytes, "file", fileName);

        var response = await client.PostAsync("/api/v1/upload/direct-stream", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions);
        Assert.NotNull(result);
        Assert.Equal(fileName, result.Name);
    }

    [Fact]
    public async Task GetFileMetadata_ReturnsCompleteMetadata()
    {
        var client = await CreateAuthenticatedClientAsync();
        var fileContent = "Metadata retrieval verification.";
        var fileName = "meta_" + Guid.NewGuid().ToString("N")[..8] + ".pdf";

        using var content = new MultipartFormDataContent();
        var fileBytes = new ByteArrayContent(Encoding.UTF8.GetBytes(fileContent));
        fileBytes.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileBytes, "file", fileName);

        var uploadRes = await client.PostAsync("/api/v1/files/upload", content);
        var fileItem = await uploadRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions);

        var metaRes = await client.GetAsync($"/api/v1/files/{fileItem!.Id}/metadata");
        Assert.Equal(HttpStatusCode.OK, metaRes.StatusCode);

        var meta = await metaRes.Content.ReadFromJsonAsync<FileMetadataDto>(_jsonOptions);
        Assert.NotNull(meta);
        Assert.Equal(fileItem.Id, meta.Id);
        Assert.Equal(fileName, meta.Name);
        Assert.Equal("pdf", meta.Extension);
        Assert.Equal("application/pdf", meta.MimeType);
        Assert.NotEmpty(meta.Sha256Hash);
        Assert.NotEmpty(meta.StorageNodeName);
    }

    [Fact]
    public async Task DownloadFile_ReturnsExactContent()
    {
        var client = await CreateAuthenticatedClientAsync();
        var originalContent = "Exact byte-for-byte stream download verification payload!";
        var fileName = "download_" + Guid.NewGuid().ToString("N")[..8] + ".bin";

        using var content = new MultipartFormDataContent();
        var fileBytes = new ByteArrayContent(Encoding.UTF8.GetBytes(originalContent));
        content.Add(fileBytes, "file", fileName);

        var uploadRes = await client.PostAsync("/api/v1/files/upload", content);
        var fileItem = await uploadRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions);

        var downloadRes = await client.GetAsync($"/api/v1/files/{fileItem!.Id}/download");
        Assert.Equal(HttpStatusCode.OK, downloadRes.StatusCode);

        var downloadedContent = await downloadRes.Content.ReadAsStringAsync();
        Assert.Equal(originalContent, downloadedContent);
    }

    [Fact]
    public async Task UpdateFile_RenameAndToggleFavorite_ReturnsOk()
    {
        var client = await CreateAuthenticatedClientAsync();
        var fileName = "old_" + Guid.NewGuid().ToString("N")[..8] + ".txt";

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("test")), "file", fileName);

        var uploadRes = await client.PostAsync("/api/v1/files/upload", content);
        var fileItem = await uploadRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions);

        var patchRes = await client.PatchAsJsonAsync($"/api/v1/files/{fileItem!.Id}", new UpdateFileRequest
        {
            Name = "new_name.md",
            IsFavorite = true
        });

        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        var updated = await patchRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("new_name.md", updated.Name);
        Assert.Equal("md", updated.Extension);
        Assert.True(updated.IsFavorite);
    }

    [Fact]
    public async Task DeleteFile_ReturnsNoContent_AndExcludesFromSubsequentRequests()
    {
        var client = await CreateAuthenticatedClientAsync();
        var fileName = "to_delete_" + Guid.NewGuid().ToString("N")[..8] + ".txt";

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("delete me")), "file", fileName);

        var uploadRes = await client.PostAsync("/api/v1/files/upload", content);
        var fileItem = await uploadRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions);

        var deleteRes = await client.DeleteAsync($"/api/v1/files/{fileItem!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        // Subsequent download should return 404 NotFound
        var downloadRes = await client.GetAsync($"/api/v1/files/{fileItem.Id}/download");
        Assert.Equal(HttpStatusCode.NotFound, downloadRes.StatusCode);

        // Subsequent metadata should return 404 NotFound
        var metaRes = await client.GetAsync($"/api/v1/files/{fileItem.Id}/metadata");
        Assert.Equal(HttpStatusCode.NotFound, metaRes.StatusCode);
    }
}
