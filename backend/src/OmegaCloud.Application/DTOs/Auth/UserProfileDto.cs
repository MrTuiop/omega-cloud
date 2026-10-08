namespace OmegaCloud.Application.DTOs.Auth;

public class UserProfileDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public long AllocatedQuotaBytes { get; set; }
    public long UsedQuotaBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}
