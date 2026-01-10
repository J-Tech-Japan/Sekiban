using Dcb.ImmutableModels.Events.Weather;
using Dcb.ImmutableModels.States.Weather;
using Dcb.ImmutableModels.States.Weather.Deciders;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.Weather;

public class WeatherForecastProjector : ITagProjector<WeatherForecastProjector>
{
    public static string ProjectorVersion => "1.0.0";
    public static string ProjectorName => nameof(WeatherForecastProjector);

    public static ITagStatePayload Project(ITagStatePayload current, Event ev)
    {
        var state = current as WeatherForecastState ?? WeatherForecastState.Empty;

        return ev.Payload switch
        {
            WeatherForecastCreated created => WeatherForecastCreatedDecider.Create(created),
            WeatherForecastUpdated updated => state.Evolve(updated),
            WeatherForecastDeleted deleted => state.Evolve(deleted),
            LocationNameChanged changed => state.Evolve(changed),
            _ => state
        };
    }
}
