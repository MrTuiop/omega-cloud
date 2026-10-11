namespace OmegaCloud.Application.DTOs.Files;

public class FileConflictDetailDto
{
    public string FileName { get; set; } = string.Empty;
    public Guid ExistingFileId { get; set; }
    public long SizeBytes { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsShared { get; set; }
}
