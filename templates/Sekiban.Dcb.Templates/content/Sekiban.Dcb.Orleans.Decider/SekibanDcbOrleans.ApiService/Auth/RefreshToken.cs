using System.ComponentModel.DataAnnotations;

namespace SekibanDcbOrleans.ApiService.Auth;

/// <summary>
///     Refresh token entity for JWT token refresh flow.
/// </summary>
public class RefreshToken
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    ///     The refresh token string
    /// </summary>
    [Required]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    ///     The user this token belongs to
    /// </summary>
    [Required]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    ///     When the token expires
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    ///     When the token was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Whether the token has been revoked
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    ///     When the token was revoked (if applicable)
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    ///     Check if the token is still valid
    /// </summary>
    public bool IsValid => !IsRevoked && DateTime.UtcNow < ExpiresAt;
}
