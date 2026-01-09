using Microsoft.AspNetCore.Identity;

namespace SekibanDcbOrleans.ApiService.Auth;

/// <summary>
///     Application user for ASP.NET Core Identity.
///     Extend this class to add custom user properties.
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>
    ///     Display name for the user
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    ///     When the user was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
