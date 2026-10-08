namespace OmegaCloud.Domain.Entities;

public class UploadChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UploadSessionId { get; set; }
    public UploadSession UploadSession { get; set; } = null!;

    public int ChunkIndex { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public bool IsUploaded { get; set; } = true;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
