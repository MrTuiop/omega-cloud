namespace OmegaCloud.Application.DTOs.Upload;

public class InitUploadSessionRequest
{
    public string FileName { get; set; } = string.Empty;
    public long? TotalSizeBytes { get; set; }
    public long? Size { get; set; } // Sequence diagram alias
    public int? ChunkSizeBytes { get; set; }
    public int? TotalChunks { get; set; }
    public string? ExpectedSha256 { get; set; }
    public string? Sha256 { get; set; } // Sequence diagram alias
    public Guid? FolderId { get; set; }
    public Guid? TargetFolderId { get; set; } // Sequence diagram alias
    public Guid? StorageNodeId { get; set; }
    public OmegaCloud.Domain.Enums.ConflictResolutionStrategy? ConflictResolution { get; set; }
    public bool? IsShared { get; set; }
}
