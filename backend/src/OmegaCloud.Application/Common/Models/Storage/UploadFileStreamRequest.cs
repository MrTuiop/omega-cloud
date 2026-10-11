namespace OmegaCloud.Application.Common.Models.Storage;

public class UploadFileStreamRequest
{
    public Stream Content { get; set; } = Stream.Null;
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long? SizeBytes { get; set; }
    public Guid? FolderId { get; set; }
    public Guid? StorageNodeId { get; set; }
    public OmegaCloud.Domain.Enums.ConflictResolutionStrategy? ConflictResolution { get; set; }
    public bool? IsShared { get; set; }
}
