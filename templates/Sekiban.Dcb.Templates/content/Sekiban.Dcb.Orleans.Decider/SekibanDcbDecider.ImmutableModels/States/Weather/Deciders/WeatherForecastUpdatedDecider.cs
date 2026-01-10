using Dcb.ImmutableModels.Events.Weather;
namespace Dcb.ImmutableModels.States.Weather.Deciders;

/// <summary>
///     Decider for WeatherForecastUpdated event
/// </summary>
public static class WeatherForecastUpdatedDecider
{
    /// <summary>
    ///     Validate preconditions for applying WeatherForecastUpdated event
    /// </summary>
    /// <exception cref="InvalidOperationException">When forecast is deleted</exception>
    public static void Validate(this WeatherForecastState state)
    {
        if (state.IsDeleted)
        {
            throw new InvalidOperationException(
                $"Cannot update weather forecast {state.ForecastId} because it has been deleted");
        }
    }

    /// <summary>
    ///     Apply WeatherForecastUpdated event to existing state
    /// </summary>
    public static WeatherForecastState Evolve(this WeatherForecastState state, WeatherForecastUpdated updated) =>
        state with
        {
            Location = updated.Location,
            Date = updated.Date,
            TemperatureC = updated.TemperatureC,
            Summary = updated.Summary
        };
}
