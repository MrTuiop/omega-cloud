namespace OmegaCloud.Application.DTOs.Folders;

public class FolderTreeResponseDto
{
    public List<FolderDto> PersonalFolders { get; set; } = new();
    public List<FolderDto> SharedFolders { get; set; } = new();
}
