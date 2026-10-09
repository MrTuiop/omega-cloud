namespace OmegaCloud.Application.DTOs.Trash;

public class TrashFolderDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ParentFolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
