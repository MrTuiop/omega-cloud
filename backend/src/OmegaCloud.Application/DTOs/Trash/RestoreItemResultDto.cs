namespace OmegaCloud.Application.DTOs.Trash;

public class RestoreItemResultDto
{
    public Guid Id { get; set; }
    public string ItemType { get; set; } = string.Empty; // "File" or "Folder"
    public string Name { get; set; } = string.Empty;
    public Guid? RestoredToFolderId { get; set; }
    public bool RestoredToRoot { get; set; }
    public int RestoredSubfoldersCount { get; set; }
    public int RestoredFilesCount { get; set; }
    public string Message { get; set; } = string.Empty;
}
