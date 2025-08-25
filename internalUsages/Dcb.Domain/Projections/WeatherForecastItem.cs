using Orleans;
namespace Dcb.Domain.Projections;

/// <summary>
///     Weather forecast item in projection
/// </summary>
[GenerateSerializer]
public record WeatherForecastItem(
    [property: Id(0)]
    Guid ForecastId,
    [property: Id(1)]
    string Location,
    [property: Id(2)]
    DateTime Date,
    [property: Id(3)]
    int TemperatureC,
    [property: Id(4)]
    string? Summary,
    [property: Id(5)]
    DateTime LastUpdated);
