using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Domain.Entities;

public class UploadSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid? TargetFolderId { get; set; }
    public Folder? TargetFolder { get; set; }

    public Guid StorageNodeId { get; set; }
    public StorageNode StorageNode { get; set; } = null!;

    public string FileName { get; set; } = string.Empty;
    public long TotalSizeBytes { get; set; }
    public int ChunkSizeBytes { get; set; }
    public int TotalChunks { get; set; }
    public string? ExpectedSha256 { get; set; }
    public UploadSessionStatus Status { get; set; } = UploadSessionStatus.Created;
    public string TempStoragePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }

    public ICollection<UploadChunk> Chunks { get; set; } = new List<UploadChunk>();
}
