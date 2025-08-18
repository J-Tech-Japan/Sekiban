namespace Dcb.Domain.Projections;

/// <summary>
///     Weather forecast item in projection
/// </summary>
public record WeatherForecastItem(
    Guid ForecastId,
    DateTime Date,
    int TemperatureC,
    string? Summary,
    DateTime LastUpdated);
