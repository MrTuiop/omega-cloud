using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OmegaCloud.Application.DTOs.Auth;

namespace OmegaCloud.Tests.Integration;

public class AuthApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AuthApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_WithValidAdminCredentials_ShouldReturnOkWithTokensAndCookie()
    {
        var client = _factory.CreateClient();

        var loginRequest = new LoginRequest
        {
            UsernameOrEmail = CustomWebApplicationFactory.DefaultAdminUsername,
            Password = CustomWebApplicationFactory.DefaultAdminPassword,
            DeviceName = "Integration Test Client"
        };

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var loginResult = await response.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions);
        Assert.NotNull(loginResult);
        Assert.False(string.IsNullOrWhiteSpace(loginResult.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(loginResult.RefreshToken));
        Assert.Equal("admin", loginResult.User.Username);
        Assert.Equal("Admin", loginResult.User.Role);
        Assert.Equal(1_099_511_627_776L, loginResult.User.AllocatedQuotaBytes);

        // Check Set-Cookie contains refreshToken
        Assert.True(response.Headers.Contains("Set-Cookie"));
        var cookies = response.Headers.GetValues("Set-Cookie");
        Assert.Contains(cookies, c => c.Contains("refreshToken="));
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ShouldReturnUnauthorized()
    {
        var client = _factory.CreateClient();

        var loginRequest = new LoginRequest
        {
            UsernameOrEmail = CustomWebApplicationFactory.DefaultRegularUsername,
            Password = "TotallyWrongPassword123!"
        };

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentUser_WithoutToken_ShouldReturnUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FullAuthLifecycle_Login_Me_Refresh_Sessions_Revoke_Logout()
    {
        var client = _factory.CreateClient();

        // 1. Login
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            UsernameOrEmail = CustomWebApplicationFactory.DefaultRegularUsername,
            Password = CustomWebApplicationFactory.DefaultRegularPassword,
            DeviceName = "MacBook Pro M3"
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var loginResult = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions))!;
        var accessToken1 = loginResult.AccessToken;
        var refreshToken1 = loginResult.RefreshToken;

        // 2. Call /me with Bearer token
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken1);
        var meResponse = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var profile = await meResponse.Content.ReadFromJsonAsync<UserProfileDto>(_jsonOptions);
        Assert.NotNull(profile);
        Assert.Equal(CustomWebApplicationFactory.DefaultRegularUsername, profile.Username);
        Assert.Equal("User", profile.Role);
        Assert.Equal(5_242_880L, profile.UsedQuotaBytes);

        // 3. Call /sessions to check active devices
        var sessionsResponse = await client.GetAsync("/api/v1/auth/sessions");
        Assert.Equal(HttpStatusCode.OK, sessionsResponse.StatusCode);

        var sessions = await sessionsResponse.Content.ReadFromJsonAsync<List<UserSessionDto>>(_jsonOptions);
        Assert.NotNull(sessions);
        Assert.NotEmpty(sessions);
        var currentSession = sessions.FirstOrDefault(s => s.IsCurrent);
        Assert.NotNull(currentSession);
        Assert.Equal("MacBook Pro M3", currentSession.DeviceName);

        // 4. Call /refresh with refreshToken1
        client.DefaultRequestHeaders.Authorization = null; // Clear auth header to verify refresh is anonymous
        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = refreshToken1
        });
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        var refreshResult = (await refreshResponse.Content.ReadFromJsonAsync<RefreshTokenResponse>(_jsonOptions))!;
        var accessToken2 = refreshResult.AccessToken;
        var refreshToken2 = refreshResult.RefreshToken;

        Assert.NotNull(accessToken2);
        Assert.NotNull(refreshToken2);
        Assert.NotEqual(refreshToken1, refreshToken2);

        // 5. Test token reuse attack detection: attempt to use the OLD refreshToken1 again!
        var reuseAttackResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = refreshToken1
        });
        Assert.Equal(HttpStatusCode.Unauthorized, reuseAttackResponse.StatusCode);

        // 6. Because token reuse was detected, all sessions were terminated.
        // Therefore refreshToken2 should also now fail!
        var subsequentResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = refreshToken2
        });
        Assert.Equal(HttpStatusCode.Unauthorized, subsequentResponse.StatusCode);

        // 7. Re-login to get fresh session and test Logout
        var loginResponse2 = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            UsernameOrEmail = CustomWebApplicationFactory.DefaultRegularUsername,
            Password = CustomWebApplicationFactory.DefaultRegularPassword
        });
        var freshLogin = (await loginResponse2.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions))!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", freshLogin.AccessToken);
        var logoutResponse = await client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshTokenRequest
        {
            RefreshToken = freshLogin.RefreshToken
        });
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteSession_ShouldRevokeTargetSession()
    {
        var client = _factory.CreateClient();

        // Login first time
        var login1 = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            UsernameOrEmail = CustomWebApplicationFactory.DefaultRegularUsername,
            Password = CustomWebApplicationFactory.DefaultRegularPassword,
            DeviceName = "Device 1"
        });
        var token1 = (await login1.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions))!;

        // Login second time (different device)
        var login2 = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            UsernameOrEmail = CustomWebApplicationFactory.DefaultRegularUsername,
            Password = CustomWebApplicationFactory.DefaultRegularPassword,
            DeviceName = "Device 2"
        });
        var token2 = (await login2.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions))!;

        // Using Device 2 token, list sessions
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2.AccessToken);
        var sessionsResponse = await client.GetAsync("/api/v1/auth/sessions");
        var sessions = (await sessionsResponse.Content.ReadFromJsonAsync<List<UserSessionDto>>(_jsonOptions))!;

        var otherSession = sessions.FirstOrDefault(s => !s.IsCurrent);
        Assert.NotNull(otherSession);

        // Revoke other device's session
        var deleteResponse = await client.DeleteAsync($"/api/v1/auth/sessions/{otherSession.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Verify it is no longer listed in active sessions
        var updatedSessionsResponse = await client.GetAsync("/api/v1/auth/sessions");
        var updatedSessions = (await updatedSessionsResponse.Content.ReadFromJsonAsync<List<UserSessionDto>>(_jsonOptions))!;
        Assert.DoesNotContain(updatedSessions, s => s.Id == otherSession.Id);
    }

    [Fact]
    public async Task Refresh_UsingHttpOnlyCookie_ShouldSucceedAndRotateCookie()
    {
        var client = _factory.CreateClient();

        // 1. Login to get refreshToken cookie
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            UsernameOrEmail = CustomWebApplicationFactory.DefaultRegularUsername,
            Password = CustomWebApplicationFactory.DefaultRegularPassword
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var setCookie = loginResponse.Headers.GetValues("Set-Cookie").FirstOrDefault(c => c.Contains("refreshToken="));
        Assert.NotNull(setCookie);

        var cookieValue = setCookie.Split(';')[0]; // "refreshToken=..."

        // 2. Call /refresh sending ONLY the Cookie header, empty request body
        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        refreshRequest.Headers.Add("Cookie", cookieValue);
        refreshRequest.Content = JsonContent.Create(new RefreshTokenRequest());

        var refreshResponse = await client.SendAsync(refreshRequest);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        var refreshResult = await refreshResponse.Content.ReadFromJsonAsync<RefreshTokenResponse>(_jsonOptions);
        Assert.NotNull(refreshResult);
        Assert.False(string.IsNullOrWhiteSpace(refreshResult.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshResult.RefreshToken));

        // Cookie is rotated
        var newSetCookie = refreshResponse.Headers.GetValues("Set-Cookie").FirstOrDefault(c => c.Contains("refreshToken="));
        Assert.NotNull(newSetCookie);
    }

    [Fact]
    public async Task RevokeSession_NonExistentSessionId_ShouldReturnNotFound()
    {
        var client = _factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            UsernameOrEmail = CustomWebApplicationFactory.DefaultRegularUsername,
            Password = CustomWebApplicationFactory.DefaultRegularPassword
        });
        var loginResult = (await login.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions))!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);
        var randomSessionId = Guid.NewGuid();

        var deleteResponse = await client.DeleteAsync($"/api/v1/auth/sessions/{randomSessionId}");
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task GetSessions_WithoutAuthorizationHeader_ShouldReturnUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/auth/sessions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_UsingCookie_ShouldSucceedAndExpireCookie()
    {
        var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            UsernameOrEmail = CustomWebApplicationFactory.DefaultRegularUsername,
            Password = CustomWebApplicationFactory.DefaultRegularPassword
        });
        var setCookie = loginResponse.Headers.GetValues("Set-Cookie").FirstOrDefault(c => c.Contains("refreshToken="));
        var cookieValue = setCookie!.Split(';')[0];

        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logoutRequest.Headers.Add("Cookie", cookieValue);
        logoutRequest.Content = JsonContent.Create(new RefreshTokenRequest());

        var logoutResponse = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        // Check response clears cookie
        Assert.True(logoutResponse.Headers.Contains("Set-Cookie"));
        var logoutCookies = logoutResponse.Headers.GetValues("Set-Cookie");
        Assert.Contains(logoutCookies, c => c.Contains("refreshToken="));

        // Verify that the session was actually revoked in the database:
        // A subsequent attempt to refresh with this cookie MUST fail with 401 Unauthorized!
        var refreshAfterLogout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        refreshAfterLogout.Headers.Add("Cookie", cookieValue);
        refreshAfterLogout.Content = JsonContent.Create(new RefreshTokenRequest());
        var refreshResponse = await client.SendAsync(refreshAfterLogout);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task RevokeCurrentSession_ShouldDeleteRefreshTokenCookie()
    {
        var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            UsernameOrEmail = CustomWebApplicationFactory.DefaultRegularUsername,
            Password = CustomWebApplicationFactory.DefaultRegularPassword
        });
        var loginResult = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions))!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        // List sessions to get current session ID
        var sessionsResponse = await client.GetAsync("/api/v1/auth/sessions");
        var sessions = (await sessionsResponse.Content.ReadFromJsonAsync<List<UserSessionDto>>(_jsonOptions))!;
        var currentSession = sessions.First(s => s.IsCurrent);

        // Delete current session
        var deleteResponse = await client.DeleteAsync($"/api/v1/auth/sessions/{currentSession.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Verify Set-Cookie header deletes refreshToken
        Assert.True(deleteResponse.Headers.Contains("Set-Cookie"));
        var cookies = deleteResponse.Headers.GetValues("Set-Cookie");
        Assert.Contains(cookies, c => c.Contains("refreshToken="));
    }
}
