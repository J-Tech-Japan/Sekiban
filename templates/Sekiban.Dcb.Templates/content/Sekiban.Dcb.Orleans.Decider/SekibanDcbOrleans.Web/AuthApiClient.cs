using System.Net.Http.Json;

namespace SekibanDcbOrleans.Web;

/// <summary>
///     API client for authentication endpoints.
/// </summary>
public class AuthApiClient
{
    private readonly HttpClient _httpClient;

    public AuthApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    ///     Login with email and password using Cookie authentication.
    /// </summary>
    public async Task<LoginResult> LoginAsync(string email, string password)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/auth/login", new
            {
                Email = email,
                Password = password,
                UseCookies = true
            });

            if (response.IsSuccessStatusCode)
            {
                var userInfo = await response.Content.ReadFromJsonAsync<UserInfoResponse>();
                return new LoginResult(true, userInfo);
            }

            return new LoginResult(false, null, "Invalid email or password");
        }
        catch (Exception ex)
        {
            return new LoginResult(false, null, $"Login failed: {ex.Message}");
        }
    }

    /// <summary>
    ///     Logout the current user.
    /// </summary>
    public async Task<bool> LogoutAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("/auth/logout", null);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Get the authentication status.
    /// </summary>
    public async Task<UserInfoResponse?> GetAuthStatusAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<UserInfoResponse>("/auth/status");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Get current user information (requires authentication).
    /// </summary>
    public async Task<UserInfoResponse?> GetCurrentUserAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/auth/me");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<UserInfoResponse>();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
///     Result of a login attempt.
/// </summary>
public record LoginResult(bool Success, UserInfoResponse? User, string? ErrorMessage = null);

/// <summary>
///     User information response.
/// </summary>
public record UserInfoResponse(
    string Id,
    string Email,
    string? DisplayName,
    IList<string> Roles,
    bool IsAuthenticated);
