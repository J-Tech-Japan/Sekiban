using Dcb.ImmutableModels.Events.Weather;
namespace Dcb.ImmutableModels.States.Weather.Deciders;

/// <summary>
///     Decider for WeatherForecastDeleted event
/// </summary>
public static class WeatherForecastDeletedDecider
{
    /// <summary>
    ///     Validate preconditions for applying WeatherForecastDeleted event
    /// </summary>
    /// <exception cref="InvalidOperationException">When forecast is already deleted</exception>
    public static void Validate(this WeatherForecastState state)
    {
        if (state.IsDeleted)
        {
            throw new InvalidOperationException(
                $"Cannot delete weather forecast {state.ForecastId} because it has already been deleted");
        }
    }

    /// <summary>
    ///     Apply WeatherForecastDeleted event to existing state
    /// </summary>
    public static WeatherForecastState Evolve(this WeatherForecastState state, WeatherForecastDeleted _) =>
        state with { IsDeleted = true };
}
