namespace OmegaCloud.Application.DTOs.Admin;

public class CreateUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "User";
    public long? AllocatedQuotaBytes { get; set; }
}

public class UpdateUserRequest
{
    public string? Role { get; set; }
    public long? AllocatedQuotaBytes { get; set; }
    public bool? IsActive { get; set; }
}

public class AdminResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}
