namespace OmegaCloud.Domain.Entities;

public class FileItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid? FolderId { get; set; }
    public Folder? Folder { get; set; }

    public Guid StorageBlobId { get; set; }
    public StorageBlob StorageBlob { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public bool IsFavorite { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
