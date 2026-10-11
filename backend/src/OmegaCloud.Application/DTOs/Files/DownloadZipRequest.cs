namespace OmegaCloud.Application.DTOs.Files;

public class DownloadZipRequest
{
    public List<Guid> FileIds { get; set; } = new();
    public List<Guid>? FolderIds { get; set; }
}
