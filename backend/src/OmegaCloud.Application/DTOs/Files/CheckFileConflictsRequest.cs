namespace OmegaCloud.Application.DTOs.Files;

public class CheckFileConflictsRequest
{
    public Guid? FolderId { get; set; }
    public List<string> FileNames { get; set; } = new();
    public bool? IsShared { get; set; }
}
