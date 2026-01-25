using System.ComponentModel.DataAnnotations;

namespace SekibanDcbDeciderAws.ApiService.Auth;

/// <summary>
///     Request for user registration
/// </summary>
public record RegisterRequest(
    [Required][EmailAddress] string Email,
    [Required][MinLength(8)] string Password,
    string? DisplayName);

/// <summary>
///     Request for login (Cookie or JWT)
/// </summary>
public record LoginRequest(
    [Required][EmailAddress] string Email,
    [Required] string Password,
    bool UseCookies = true);

/// <summary>
///     Request for token refresh
/// </summary>
public record RefreshTokenRequest(
    [Required] string AccessToken,
    [Required] string RefreshToken);

/// <summary>
///     Response containing JWT tokens
/// </summary>
public record TokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpires,
    DateTime RefreshTokenExpires);

/// <summary>
///     Response containing user information
/// </summary>
public record UserInfoResponse(
    string Id,
    string Email,
    string? DisplayName,
    IList<string> Roles,
    bool IsAuthenticated);

/// <summary>
///     Generic auth result response
/// </summary>
public record AuthResult(
    bool Succeeded,
    string? Message = null,
    IEnumerable<string>? Errors = null);
