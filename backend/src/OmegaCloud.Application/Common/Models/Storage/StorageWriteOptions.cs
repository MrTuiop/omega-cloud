namespace OmegaCloud.Application.Common.Models.Storage;

public class StorageWriteOptions
{
    public Guid? BlobId { get; set; }
    public string? ExpectedSha256 { get; set; }
    public long? ExpectedSize { get; set; }
    public string? ContentType { get; set; }
}
