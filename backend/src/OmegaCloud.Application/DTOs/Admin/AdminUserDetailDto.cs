using OmegaCloud.Application.DTOs.Auth;

namespace OmegaCloud.Application.DTOs.Admin;

public class AdminUserDetailDto : AdminUserDto
{
    public IReadOnlyList<UserSessionDto> Sessions { get; set; } = Array.Empty<UserSessionDto>();
}
