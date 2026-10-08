namespace OmegaCloud.Domain.Entities;

public class StorageBlob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StorageNodeId { get; set; }
    public StorageNode StorageNode { get; set; } = null!;

    public string Sha256Hash { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StorageRelativePath { get; set; } = string.Empty;
    public int ReferenceCount { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<FileItem> FileItems { get; set; } = new List<FileItem>();
}
