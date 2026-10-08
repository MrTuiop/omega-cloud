using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OmegaCloud.Application.Common.Exceptions;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Auth;
using OmegaCloud.Application.Services;
using OmegaCloud.Infrastructure.Security;

namespace OmegaCloud.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    public const string RefreshTokenCookieName = "refreshToken";

    private readonly IAuthService _authService;
    private readonly ICurrentUser _currentUser;
    private readonly JwtOptions _jwtOptions;

    public AuthController(
        IAuthService authService,
        ICurrentUser currentUser,
        IOptions<JwtOptions> jwtOptions)
    {
        _authService = authService;
        _currentUser = currentUser;
        _jwtOptions = jwtOptions.Value;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        var ip = GetClientIpAddress();
        var userAgent = GetUserAgent();

        var response = await _authService.LoginAsync(request, ip, userAgent, ct);

        SetRefreshTokenCookie(response.RefreshToken);

        return Ok(response);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RefreshTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RefreshTokenResponse>> Refresh(
        [FromBody] RefreshTokenRequest? request,
        CancellationToken ct)
    {
        var token = request?.RefreshToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            Request.Cookies.TryGetValue(RefreshTokenCookieName, out token);
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new UnauthorizedException("Refresh token is required.");
        }

        var ip = GetClientIpAddress();
        var userAgent = GetUserAgent();

        var response = await _authService.RefreshTokenAsync(token, ip, userAgent, ct);

        SetRefreshTokenCookie(response.RefreshToken);

        return Ok(response);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(
        [FromBody] RefreshTokenRequest? request,
        CancellationToken ct)
    {
        var rawToken = request?.RefreshToken;
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            Request.Cookies.TryGetValue(RefreshTokenCookieName, out rawToken);
        }

        var userId = _currentUser.UserId ?? Guid.Empty;
        var sessionId = _currentUser.SessionId;

        await _authService.LogoutAsync(userId, sessionId, rawToken, ct);

        DeleteRefreshTokenCookie();

        return Ok(new { message = "Logged out successfully." });
    }

    [HttpGet("sessions")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<UserSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<UserSessionDto>>> GetSessions(CancellationToken ct)
    {
        var userId = GetRequiredUserId();
        var currentSessionId = _currentUser.SessionId;

        var sessions = await _authService.GetUserSessionsAsync(userId, currentSessionId, ct);
        return Ok(sessions);
    }

    [HttpDelete("sessions/{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeSession(
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        var userId = GetRequiredUserId();

        await _authService.RevokeSessionAsync(userId, id, ct);

        if (_currentUser.SessionId == id)
        {
            DeleteRefreshTokenCookie();
        }

        return Ok(new { message = "Session revoked successfully." });
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileDto>> GetCurrentUser(CancellationToken ct)
    {
        var userId = GetRequiredUserId();

        var profile = await _authService.GetCurrentUserProfileAsync(userId, ct);
        return Ok(profile);
    }

    private Guid GetRequiredUserId()
    {
        if (!_currentUser.UserId.HasValue)
        {
            throw new UnauthorizedException("User is not authenticated.");
        }

        return _currentUser.UserId.Value;
    }

    private string GetClientIpAddress()
    {
        if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            var ip = forwarded.ToString().Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(ip))
            {
                return ip;
            }
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private string GetUserAgent()
    {
        return Request.Headers["User-Agent"].ToString();
    }

    private void SetRefreshTokenCookie(string refreshToken)
    {
        Response.Cookies.Append(RefreshTokenCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays),
            Path = "/"
        });
    }

    private void DeleteRefreshTokenCookie()
    {
        Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
        {
            Path = "/"
        });
    }
}
