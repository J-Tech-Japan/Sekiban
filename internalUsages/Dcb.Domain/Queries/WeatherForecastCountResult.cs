namespace Dcb.Domain.Queries;

/// <summary>
/// Result containing weather forecast counts
/// </summary>
public record WeatherForecastCountResult(
    int SafeVersion,
    int UnsafeVersion,
    int TotalCount
);