using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.Weather;

/// <summary>
///     Request model for changing location name
/// </summary>
public record ChangeLocationNameRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string NewLocationName { get; init; } = string.Empty;
}
