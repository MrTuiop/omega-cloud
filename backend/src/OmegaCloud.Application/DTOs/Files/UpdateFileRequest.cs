namespace OmegaCloud.Application.DTOs.Files;

public class UpdateFileRequest
{
    public string? Name { get; set; }
    public Guid? FolderId { get; set; }
    public bool? MoveToRoot { get; set; }
    public bool? IsFavorite { get; set; }
}
