namespace OmegaCloud.Application.DTOs.Trash;

public class TrashCleanupResultDto
{
    public int PurgedFilesCount { get; set; }
    public int PurgedFoldersCount { get; set; }
    public long FreedBytes { get; set; }
    public int PurgedBlobsCount { get; set; }
}
