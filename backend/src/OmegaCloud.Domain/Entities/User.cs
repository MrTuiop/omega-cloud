using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.User;
    public long AllocatedQuotaBytes { get; set; } = 107_374_182_400L; // 100 GB default
    public long UsedQuotaBytes { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();
}
