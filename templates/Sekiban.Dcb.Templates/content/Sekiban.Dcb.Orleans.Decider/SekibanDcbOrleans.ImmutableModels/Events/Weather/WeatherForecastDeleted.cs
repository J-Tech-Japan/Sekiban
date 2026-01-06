using Dcb.ImmutableModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.ImmutableModels.Events.Weather;

public record WeatherForecastDeleted(Guid ForecastId) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new WeatherForecastTag(ForecastId));
}
