namespace OmegaCloud.Application.DTOs.Trash;

public class EmptyTrashResultDto
{
    public int DeletedFilesCount { get; set; }
    public int DeletedFoldersCount { get; set; }
    public long FreedBytes { get; set; }
}
