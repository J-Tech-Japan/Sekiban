using Sekiban.Dcb.Tags;
namespace Dcb.ImmutableModels.States.Weather;

public record WeatherForecastState(
    Guid ForecastId,
    string Location,
    DateOnly Date,
    int TemperatureC,
    string? Summary,
    bool IsDeleted = false)
    : ITagStatePayload
{
    public static WeatherForecastState Empty => new(Guid.Empty, string.Empty, DateOnly.MinValue, 0, null);
}
