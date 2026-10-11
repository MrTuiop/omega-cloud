namespace OmegaCloud.Application.DTOs.Admin;

public class AdminUserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public long AllocatedQuotaBytes { get; set; }
    public long UsedQuotaBytes { get; set; }
    public double QuotaPercent => AllocatedQuotaBytes > 0 ? Math.Round((double)UsedQuotaBytes / AllocatedQuotaBytes * 100, 2) : 0;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastActiveAt { get; set; }
    public int ActiveSessionsCount { get; set; }
}
