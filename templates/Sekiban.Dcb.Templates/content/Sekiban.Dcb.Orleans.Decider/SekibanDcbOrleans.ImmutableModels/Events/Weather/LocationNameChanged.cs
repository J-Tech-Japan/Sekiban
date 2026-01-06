using Dcb.ImmutableModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.ImmutableModels.Events.Weather;

public record LocationNameChanged(Guid ForecastId, string NewLocationName, string OldLocationName) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new WeatherForecastTag(ForecastId));
}
