namespace OmegaCloud.Domain.Entities;

public class Folder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid? ParentFolderId { get; set; }
    public Folder? ParentFolder { get; set; }
    public ICollection<Folder> SubFolders { get; set; } = new List<Folder>();

    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool IsShared { get; set; } = false;
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<FileItem> FileItems { get; set; } = new List<FileItem>();
}
