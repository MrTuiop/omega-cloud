using System.Security.Claims;
using OmegaCloud.Application.Common.Interfaces;

namespace OmegaCloud.Api.Services;

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var idClaim = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? user?.FindFirst("sub")?.Value;

            return Guid.TryParse(idClaim, out var id) ? id : null;
        }
    }

    public string? Username => _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Name)?.Value
                           ?? _httpContextAccessor.HttpContext?.User?.FindFirst("unique_name")?.Value
                           ?? _httpContextAccessor.HttpContext?.User?.FindFirst("username")?.Value;

    public Guid? SessionId
    {
        get
        {
            var sessionClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("session_id")?.Value;
            return Guid.TryParse(sessionClaim, out var id) ? id : null;
        }
    }

    public string? Role => _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Role)?.Value
                           ?? _httpContextAccessor.HttpContext?.User?.FindFirst("role")?.Value;

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
}
