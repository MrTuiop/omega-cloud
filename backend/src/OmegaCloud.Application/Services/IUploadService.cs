using OmegaCloud.Application.Common.Models.Storage;
using OmegaCloud.Application.DTOs.Files;
using OmegaCloud.Application.DTOs.Upload;

namespace OmegaCloud.Application.Services;

public interface IUploadService
{
    Task<FileItemDto> DirectStreamUploadAsync(UploadFileStreamRequest request, Guid userId, CancellationToken ct = default);
    Task<UploadSessionDto> InitSessionAsync(InitUploadSessionRequest request, Guid userId, CancellationToken ct = default);
    Task<UploadChunkResultDto> UploadChunkAsync(Guid sessionId, int chunkIndex, Stream chunkStream, long? contentLength, string? expectedChunkSha256, Guid userId, CancellationToken ct = default);
    Task<FileItemDto> CompleteSessionAsync(Guid sessionId, CompleteUploadSessionRequest? request, Guid userId, CancellationToken ct = default);
    Task<UploadSessionDto> GetSessionStatusAsync(Guid sessionId, Guid userId, int chunkIndex = 0, CancellationToken ct = default);
    Task AbortSessionAsync(Guid sessionId, Guid userId, CancellationToken ct = default);
}
