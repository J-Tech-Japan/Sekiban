using Orleans;
using Sekiban.Pure.Events;

namespace DaprSample.Domain.Aggregates.WeatherForecasts.Events;

[GenerateSerializer]
public record WeatherForecastDeleted : IEventPayload
{
    public static WeatherForecastDeleted Instance => new();
}