using Dcb.ImmutableModels.Events.Weather;
namespace Dcb.ImmutableModels.States.Weather.Deciders;

/// <summary>
///     Decider for WeatherForecastCreated event
/// </summary>
public static class WeatherForecastCreatedDecider
{
    /// <summary>
    ///     Create a new WeatherForecastState from WeatherForecastCreated event
    /// </summary>
    public static WeatherForecastState Create(WeatherForecastCreated created) =>
        new(
            created.ForecastId,
            created.Location,
            created.Date,
            created.TemperatureC,
            created.Summary);
}
