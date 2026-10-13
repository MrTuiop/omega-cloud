namespace OmegaCloud.Application.DTOs.Files;

public class SaveToPersonalRequest
{
    public List<Guid>? FileIds { get; set; }
    public List<Guid>? FolderIds { get; set; }
    public Guid? TargetFolderId { get; set; }
    public Guid? TargetSharedFolderId { get; set; }
}
