namespace Omega.StorageNode.Models;

public class StorageBlobResult
{
    public Guid BlobId { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StorageRelativePath { get; set; } = string.Empty;
}
