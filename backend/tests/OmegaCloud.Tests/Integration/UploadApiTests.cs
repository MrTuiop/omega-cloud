using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OmegaCloud.Application.DTOs.Auth;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Application.DTOs.Folders;
using OmegaCloud.Application.DTOs.Upload;

namespace OmegaCloud.Tests.Integration;

public class UploadApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public UploadApiTests(CustomWebApplicationFactory factory)
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
            DeviceName = "Upload-Api-Test"
        });
        loginResponse.EnsureSuccessStatusCode();

        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult!.AccessToken);
        return client;
    }

    [Fact]
    public async Task DirectStreamUpload_WithoutToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var content = new StringContent("test", Encoding.UTF8, "text/plain");

        var response = await client.PostAsync("/api/v1/upload/direct-stream", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DirectStreamUpload_ViaPipeReader_RawOctetStream_ReturnsCreated()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var fileContent = "This is raw octet-stream uploaded directly through PipeReader!";
        var fileName = "pipereader_" + Guid.NewGuid().ToString("N")[..8] + ".txt";

        var requestContent = new ByteArrayContent(Encoding.UTF8.GetBytes(fileContent));
        requestContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        requestContent.Headers.Add("X-File-Name", fileName);

        // Act
        var response = await client.PostAsync("/api/v1/upload/direct-stream", requestContent);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions);
        Assert.NotNull(result);
        Assert.Equal(fileName, result.Name);
        Assert.Equal(Encoding.UTF8.GetByteCount(fileContent), result.SizeBytes);

        // Verify download content matches
        var downloadRes = await client.GetAsync($"/api/v1/files/{result.Id}/download");
        Assert.Equal(HttpStatusCode.OK, downloadRes.StatusCode);
        var downloadedText = await downloadRes.Content.ReadAsStringAsync();
        Assert.Equal(fileContent, downloadedText);
    }

    [Fact]
    public async Task DirectStreamUpload_ViaPipeReader_MissingFileName_ReturnsBadRequest()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var requestContent = new ByteArrayContent(Encoding.UTF8.GetBytes("No file name provided"));
        requestContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        // Act
        var response = await client.PostAsync("/api/v1/upload/direct-stream", requestContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetUserQuota_ReturnsCurrentQuotaStats()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        // Act
        var response = await client.GetAsync("/api/v1/upload/quota");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var quota = await response.Content.ReadFromJsonAsync<UserQuotaDto>(_jsonOptions);
        Assert.NotNull(quota);
        Assert.True(quota.AllocatedBytes > 0);
        Assert.True(quota.AvailableBytes >= 0);
    }

    [Fact]
    public async Task ResumableChunkSession_FullWorkflow_Succeeds()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        var chunk0Text = "Part0: Hello from chunk zero. ";
        var chunk1Text = "Part1: Hello from chunk one! Finished assembling.";
        var fullText = chunk0Text + chunk1Text;
        var fullBytes = Encoding.UTF8.GetBytes(fullText);
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(fullBytes)).ToLowerInvariant();
        var fileName = "resumable_" + Guid.NewGuid().ToString("N")[..8] + ".txt";

        var chunk0Bytes = Encoding.UTF8.GetBytes(chunk0Text);
        var chunk1Bytes = Encoding.UTF8.GetBytes(chunk1Text);

        // Step 1: Initialize session
        var initRequest = new InitUploadSessionRequest
        {
            FileName = fileName,
            TotalSizeBytes = fullBytes.Length,
            ChunkSizeBytes = chunk0Bytes.Length,
            TotalChunks = 2,
            ExpectedSha256 = expectedSha256
        };

        var initResponse = await client.PostAsJsonAsync("/api/v1/upload/sessions/init", initRequest);
        Assert.Equal(HttpStatusCode.OK, initResponse.StatusCode);

        var session = await initResponse.Content.ReadFromJsonAsync<UploadSessionDto>(_jsonOptions);
        Assert.NotNull(session);
        Assert.Equal(fileName, session.FileName);
        Assert.Equal(fullBytes.Length, session.TotalSizeBytes);
        Assert.Equal(2, session.TotalChunks);
        Assert.Equal("Created", session.Status);

        // Step 2: Verify session status before uploading chunks
        var statusBefore = await client.GetFromJsonAsync<UploadSessionDto>($"/api/v1/upload/sessions/{session.SessionId}", _jsonOptions);
        Assert.NotNull(statusBefore);
        Assert.Empty(statusBefore.UploadedChunks);
        Assert.Equal(new[] { 0, 1 }, statusBefore.MissingChunks);
        Assert.Equal(0, statusBefore.ProgressPercentage);

        // Step 3: Upload chunk 0 via PUT raw body
        var chunk0Content = new ByteArrayContent(chunk0Bytes);
        chunk0Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var chunk0Res = await client.PutAsync($"/api/v1/upload/sessions/{session.SessionId}/chunks/0", chunk0Content);
        Assert.Equal(HttpStatusCode.OK, chunk0Res.StatusCode);

        var chunk0Dto = await chunk0Res.Content.ReadFromJsonAsync<UploadChunkResultDto>(_jsonOptions);
        Assert.NotNull(chunk0Dto);
        Assert.Equal(0, chunk0Dto.ChunkIndex);
        Assert.Equal("Saved", chunk0Dto.Status);

        // Step 4: Verify session status mid-upload
        var statusMid = await client.GetFromJsonAsync<UploadSessionDto>($"/api/v1/upload/sessions/{session.SessionId}", _jsonOptions);
        Assert.NotNull(statusMid);
        Assert.Single(statusMid.UploadedChunks);
        Assert.Equal(0, statusMid.UploadedChunks[0]);
        Assert.Single(statusMid.MissingChunks);
        Assert.Equal(1, statusMid.MissingChunks[0]);

        // Step 5: Upload chunk 1 via PUT multipart
        using var multipartContent = new MultipartFormDataContent();
        var part1Content = new ByteArrayContent(chunk1Bytes);
        multipartContent.Add(part1Content, "chunk", "chunk1.part");
        var chunk1Res = await client.PutAsync($"/api/v1/upload/sessions/{session.SessionId}/chunks/1", multipartContent);
        Assert.Equal(HttpStatusCode.OK, chunk1Res.StatusCode);

        // Step 6: Verify 100% progress
        var statusFull = await client.GetFromJsonAsync<UploadSessionDto>($"/api/v1/upload/sessions/{session.SessionId}", _jsonOptions);
        Assert.NotNull(statusFull);
        Assert.Equal(2, statusFull.UploadedChunks.Count);
        Assert.Empty(statusFull.MissingChunks);
        Assert.Equal(100.0, statusFull.ProgressPercentage);

        // Step 7: Complete session
        var completeRes = await client.PostAsJsonAsync($"/api/v1/upload/sessions/{session.SessionId}/complete", new CompleteUploadSessionRequest());
        Assert.Equal(HttpStatusCode.Created, completeRes.StatusCode);

        var fileItem = await completeRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions);
        Assert.NotNull(fileItem);
        Assert.Equal(fileName, fileItem.Name);
        Assert.Equal(fullBytes.Length, fileItem.SizeBytes);

        // Step 8: Download assembled file and verify content
        var downloadRes = await client.GetAsync($"/api/v1/files/{fileItem.Id}/download");
        Assert.Equal(HttpStatusCode.OK, downloadRes.StatusCode);
        var downloadedText = await downloadRes.Content.ReadAsStringAsync();
        Assert.Equal(fullText, downloadedText);
    }

    [Fact]
    public async Task ResumableChunkSession_OutOfOrderChunks_AssemblesCorrectly()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        var part0 = "00000_";
        var part1 = "11111_";
        var part2 = "22222";
        var full = part0 + part1 + part2;
        var fullBytes = Encoding.UTF8.GetBytes(full);
        var fileName = "outoforder_" + Guid.NewGuid().ToString("N")[..8] + ".txt";

        var initRequest = new InitUploadSessionRequest
        {
            FileName = fileName,
            TotalSizeBytes = fullBytes.Length,
            ChunkSizeBytes = 6,
            TotalChunks = 3
        };

        var initRes = await client.PostAsJsonAsync("/api/v1/upload/sessions/init", initRequest);
        var session = await initRes.Content.ReadFromJsonAsync<UploadSessionDto>(_jsonOptions);

        // Upload chunk 2 first!
        var c2 = new ByteArrayContent(Encoding.UTF8.GetBytes(part2));
        var r2 = await client.PutAsync($"/api/v1/upload/sessions/{session!.SessionId}/chunks/2", c2);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        // Upload chunk 0 next!
        var c0 = new ByteArrayContent(Encoding.UTF8.GetBytes(part0));
        var r0 = await client.PutAsync($"/api/v1/upload/sessions/{session.SessionId}/chunks/0", c0);
        Assert.Equal(HttpStatusCode.OK, r0.StatusCode);

        // Upload chunk 1 last!
        var c1 = new ByteArrayContent(Encoding.UTF8.GetBytes(part1));
        var r1 = await client.PutAsync($"/api/v1/upload/sessions/{session.SessionId}/chunks/1", c1);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        // Complete session
        var compRes = await client.PostAsJsonAsync($"/api/v1/upload/sessions/{session.SessionId}/complete", new CompleteUploadSessionRequest());
        Assert.Equal(HttpStatusCode.Created, compRes.StatusCode);

        var fileItem = await compRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions);

        // Verify order: must be "00000_11111_22222"
        var downloadRes = await client.GetAsync($"/api/v1/files/{fileItem!.Id}/download");
        var downloadedText = await downloadRes.Content.ReadAsStringAsync();
        Assert.Equal(full, downloadedText);
    }

    [Fact]
    public async Task ResumableChunkSession_CompleteWithMissingChunks_ReturnsBadRequest()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var fileName = "incomplete_" + Guid.NewGuid().ToString("N")[..8] + ".bin";

        var initRes = await client.PostAsJsonAsync("/api/v1/upload/sessions/init", new InitUploadSessionRequest
        {
            FileName = fileName,
            TotalSizeBytes = 100,
            ChunkSizeBytes = 50,
            TotalChunks = 2
        });
        var session = await initRes.Content.ReadFromJsonAsync<UploadSessionDto>(_jsonOptions);

        // Upload only chunk 0
        var c0 = new ByteArrayContent(new byte[50]);
        await client.PutAsync($"/api/v1/upload/sessions/{session!.SessionId}/chunks/0", c0);

        // Attempt complete
        var compRes = await client.PostAsJsonAsync($"/api/v1/upload/sessions/{session.SessionId}/complete", new CompleteUploadSessionRequest());
        Assert.Equal(HttpStatusCode.BadRequest, compRes.StatusCode);
    }

    [Fact]
    public async Task ResumableChunkSession_Abort_CancelsSessionAndRejectsChunks()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var fileName = "abort_" + Guid.NewGuid().ToString("N")[..8] + ".bin";

        var initRes = await client.PostAsJsonAsync("/api/v1/upload/sessions/init", new InitUploadSessionRequest
        {
            FileName = fileName,
            TotalSizeBytes = 100,
            ChunkSizeBytes = 50,
            TotalChunks = 2
        });
        var session = await initRes.Content.ReadFromJsonAsync<UploadSessionDto>(_jsonOptions);

        // Abort session via DELETE
        var abortRes = await client.DeleteAsync($"/api/v1/upload/sessions/{session!.SessionId}");
        Assert.Equal(HttpStatusCode.NoContent, abortRes.StatusCode);

        // Subsequent chunk upload should fail
        var c0 = new ByteArrayContent(new byte[50]);
        var chunkRes = await client.PutAsync($"/api/v1/upload/sessions/{session.SessionId}/chunks/0", c0);
        Assert.Equal(HttpStatusCode.BadRequest, chunkRes.StatusCode);
    }

    [Fact]
    public async Task ResumableChunkSession_ChunkSha256Mismatch_ReturnsBadRequest()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var fileName = "mismatch_" + Guid.NewGuid().ToString("N")[..8] + ".bin";

        var initRes = await client.PostAsJsonAsync("/api/v1/upload/sessions/init", new InitUploadSessionRequest
        {
            FileName = fileName,
            TotalSizeBytes = 50,
            ChunkSizeBytes = 50,
            TotalChunks = 1
        });
        var session = await initRes.Content.ReadFromJsonAsync<UploadSessionDto>(_jsonOptions);

        var c0 = new ByteArrayContent(Encoding.UTF8.GetBytes("Data"));
        c0.Headers.Add("X-Chunk-Sha256", "0000000000000000000000000000000000000000000000000000000000000000");

        var chunkRes = await client.PutAsync($"/api/v1/upload/sessions/{session!.SessionId}/chunks/0", c0);
        Assert.Equal(HttpStatusCode.BadRequest, chunkRes.StatusCode);
    }

    [Fact]
    public async Task DirectStreamUpload_ViaPipeReader_ContentDispositionHeader_ReturnsCreated()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var content = "Content-Disposition streaming payload";
        var fileName = "disposition_" + Guid.NewGuid().ToString("N")[..8] + ".txt";

        var byteContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        byteContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileName = $"\"{fileName}\""
        };

        // Act
        var response = await client.PostAsync("/api/v1/upload/direct-stream", byteContent);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions);
        Assert.NotNull(result);
        Assert.Equal(fileName, result.Name);
    }

    [Fact]
    public async Task ResumableChunkSession_UploadsIntoSpecificFolder_ReflectsInFolderContent()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        // Create folder
        var folderName = "UploadTargetFolder_" + Guid.NewGuid().ToString("N")[..8];
        var createFolderRes = await client.PostAsJsonAsync("/api/v1/folders", new CreateFolderRequest
        {
            Name = folderName
        });
        createFolderRes.EnsureSuccessStatusCode();
        var folderDto = await createFolderRes.Content.ReadFromJsonAsync<FolderDto>(_jsonOptions);

        var filePayload = "File inside custom folder!";
        var fileBytes = Encoding.UTF8.GetBytes(filePayload);
        var fileName = "folder_file_" + Guid.NewGuid().ToString("N")[..8] + ".txt";

        // Init session in folder
        var initRes = await client.PostAsJsonAsync("/api/v1/upload/sessions/init", new InitUploadSessionRequest
        {
            FileName = fileName,
            TotalSizeBytes = fileBytes.Length,
            TotalChunks = 1,
            FolderId = folderDto!.Id
        });
        Assert.Equal(HttpStatusCode.OK, initRes.StatusCode);
        var session = await initRes.Content.ReadFromJsonAsync<UploadSessionDto>(_jsonOptions);

        // Upload chunk
        var chunkContent = new ByteArrayContent(fileBytes);
        await client.PutAsync($"/api/v1/upload/sessions/{session!.SessionId}/chunks/0", chunkContent);

        // Complete session
        var compRes = await client.PostAsJsonAsync($"/api/v1/upload/sessions/{session.SessionId}/complete", new CompleteUploadSessionRequest());
        Assert.Equal(HttpStatusCode.Created, compRes.StatusCode);
        var fileItem = await compRes.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions);
        Assert.NotNull(fileItem);
        Assert.Equal(folderDto.Id, fileItem.FolderId);

        // Verify folder contents includes this file
        var folderContentRes = await client.GetAsync($"/api/v1/folders/{folderDto.Id}");
        var folderContent = await folderContentRes.Content.ReadFromJsonAsync<FolderContentDto>(_jsonOptions);
        Assert.NotNull(folderContent);
        Assert.Contains(folderContent.Files, f => f.Id == fileItem.Id);
    }

    [Fact]
    public async Task DirectStreamUpload_ChunkedTransfer_NoContentLength_ReturnsCreated()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        var fileContent = "This is chunked stream data without Content-Length header!";
        var fileName = "chunked_" + Guid.NewGuid().ToString("N")[..8] + ".txt";

        var bytes = Encoding.UTF8.GetBytes(fileContent);
        using var stream = new NonSeekableMemoryStream(bytes);
        var requestContent = new StreamContent(stream);
        requestContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        requestContent.Headers.Add("X-File-Name", fileName);

        // Act
        var response = await client.PostAsync("/api/v1/upload/direct-stream", requestContent);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<FileItemDto>(_jsonOptions);
        Assert.NotNull(result);
        Assert.Equal(fileName, result.Name);
        Assert.Equal(bytes.Length, result.SizeBytes);

        // Verify download content
        var downloadRes = await client.GetAsync($"/api/v1/files/{result.Id}/download");
        Assert.Equal(HttpStatusCode.OK, downloadRes.StatusCode);
        var downloadedText = await downloadRes.Content.ReadAsStringAsync();
        Assert.Equal(fileContent, downloadedText);
    }

    private class NonSeekableMemoryStream : MemoryStream
    {
        public NonSeekableMemoryStream(byte[] buffer) : base(buffer) { }
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    }
}
