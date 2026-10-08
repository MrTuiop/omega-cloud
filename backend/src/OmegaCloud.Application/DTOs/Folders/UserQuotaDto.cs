namespace OmegaCloud.Application.DTOs.Folders;

public class UserQuotaDto
{
    public long AllocatedBytes { get; set; }
    public long UsedBytes { get; set; }
    public long AvailableBytes { get; set; }
}
