namespace Dcb.Domain.Projections;

/// <summary>
///     Weather forecast item in projection
/// </summary>
[global::Orleans.GenerateSerializer]
public record WeatherForecastItem(
    [property: global::Orleans.Id(0)] Guid ForecastId,
    [property: global::Orleans.Id(1)] string Location,
    [property: global::Orleans.Id(2)] DateTime Date,
    [property: global::Orleans.Id(3)] int TemperatureC,
    [property: global::Orleans.Id(4)] string? Summary,
    [property: global::Orleans.Id(5)] DateTime LastUpdated);
