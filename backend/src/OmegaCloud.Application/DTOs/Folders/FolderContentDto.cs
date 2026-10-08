using OmegaCloud.Application.DTOs.Files;

namespace OmegaCloud.Application.DTOs.Folders;

public class FolderContentDto
{
    public FolderDto? CurrentFolder { get; set; }
    public List<BreadcrumbDto> Breadcrumbs { get; set; } = new();
    public List<FolderDto> Folders { get; set; } = new();
    public List<FileItemDto> Files { get; set; } = new();
    public UserQuotaDto Quota { get; set; } = new();
}
