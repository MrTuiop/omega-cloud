namespace Omega.StorageNode.Models;

public class AssembleChunksRequest
{
    public int TotalChunks { get; set; }
    public string? ExpectedSha256 { get; set; }
    public Guid? BlobId { get; set; }
}
