namespace OmegaCloud.Application.DTOs.Files;

public class BulkDeleteRequest
{
    public List<Guid>? FileIds { get; set; }
    public List<Guid>? FolderIds { get; set; }
}
