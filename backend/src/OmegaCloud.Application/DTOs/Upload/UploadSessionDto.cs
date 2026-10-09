namespace OmegaCloud.Application.DTOs.Upload;

public class UploadSessionDto
{
    public Guid SessionId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long TotalSizeBytes { get; set; }
    public int ChunkSizeBytes { get; set; }
    public int TotalChunks { get; set; }
    public string? ExpectedSha256 { get; set; }
    public string UploadMode { get; set; } = "Proxy";
    public string? TargetUrl { get; set; }
    public string Status { get; set; } = "Created";
    public Guid? TargetFolderId { get; set; }
    public IReadOnlyList<int> UploadedChunks { get; set; } = Array.Empty<int>();
    public IReadOnlyList<int> MissingChunks { get; set; } = Array.Empty<int>();
    public double ProgressPercentage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
