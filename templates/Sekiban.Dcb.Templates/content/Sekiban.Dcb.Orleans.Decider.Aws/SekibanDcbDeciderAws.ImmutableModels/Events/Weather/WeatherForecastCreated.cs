using Dcb.ImmutableModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.ImmutableModels.Events.Weather;

public record WeatherForecastCreated(Guid ForecastId, string Location, DateOnly Date, int TemperatureC, string? Summary)
    : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new WeatherForecastTag(ForecastId));
}
