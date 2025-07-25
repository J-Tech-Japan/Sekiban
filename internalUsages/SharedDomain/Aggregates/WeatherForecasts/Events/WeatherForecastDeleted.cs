using Orleans;
using Sekiban.Pure.Events;

namespace SharedDomain.Aggregates.WeatherForecasts.Events;

[GenerateSerializer]
public record WeatherForecastDeleted : IEventPayload
{
    public static WeatherForecastDeleted Instance => new();
}