namespace OmegaCloud.Application.DTOs.Folders;

public class UpdateFolderRequest
{
    public string? Name { get; set; }
    public Guid? ParentFolderId { get; set; }
    public bool? MoveToRoot { get; set; }
    public string? Color { get; set; }
}
