namespace OmegaCloud.Application.DTOs.Folders;

public class CreateFolderRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid? ParentFolderId { get; set; }
    public string? Color { get; set; }
    public bool? IsShared { get; set; }
}
