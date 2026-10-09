using OmegaCloud.Application.DTOs.Folders;

namespace OmegaCloud.Application.DTOs.Trash;

public class TrashContentDto
{
    public List<TrashFolderDto> Folders { get; set; } = new();
    public List<TrashFileDto> Files { get; set; } = new();
    public long TotalTrashSizeBytes { get; set; }
    public int TotalItemCount { get; set; }
    public UserQuotaDto Quota { get; set; } = null!;
}
