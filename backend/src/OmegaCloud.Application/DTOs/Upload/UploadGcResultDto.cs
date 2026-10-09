namespace OmegaCloud.Application.DTOs.Upload;

public class UploadGcResultDto
{
    public int ExpiredSessionsCount { get; set; }
    public int CleanedTempFoldersCount { get; set; }
    public int RemovedOldSessionsCount { get; set; }
}
