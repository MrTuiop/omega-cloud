namespace OmegaCloud.Application.DTOs.Files;

public class FileMetadataDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? FolderId { get; set; }
    public string? FolderName { get; set; }
    public Guid StorageBlobId { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public Guid StorageNodeId { get; set; }
    public string StorageNodeName { get; set; } = string.Empty;
    public string StorageNodeType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public bool IsShared { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
