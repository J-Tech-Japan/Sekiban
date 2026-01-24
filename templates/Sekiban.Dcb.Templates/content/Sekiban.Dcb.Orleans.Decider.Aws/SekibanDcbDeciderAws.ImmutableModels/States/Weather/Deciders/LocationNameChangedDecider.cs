using Dcb.ImmutableModels.Events.Weather;
namespace Dcb.ImmutableModels.States.Weather.Deciders;

/// <summary>
///     Decider for LocationNameChanged event
/// </summary>
public static class LocationNameChangedDecider
{
    /// <summary>
    ///     Validate preconditions for applying LocationNameChanged event
    /// </summary>
    /// <param name="state">Current state</param>
    /// <param name="newLocationName">New location name to validate</param>
    /// <exception cref="InvalidOperationException">When forecast is deleted or location is same</exception>
    public static void Validate(this WeatherForecastState state, string newLocationName)
    {
        if (state.IsDeleted)
        {
            throw new InvalidOperationException(
                $"Cannot change location name for weather forecast {state.ForecastId} because it has been deleted");
        }

        if (state.Location == newLocationName)
        {
            throw new InvalidOperationException(
                $"Location name is already '{newLocationName}'");
        }
    }

    /// <summary>
    ///     Apply LocationNameChanged event to existing state
    /// </summary>
    public static WeatherForecastState Evolve(this WeatherForecastState state, LocationNameChanged changed) =>
        state with { Location = changed.NewLocationName };
}
