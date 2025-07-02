using Orleans;
using Sekiban.Pure.Events;

namespace SharedDomain.Aggregates.WeatherForecasts.Events;

[GenerateSerializer]
public record WeatherForecastLocationUpdated(
    [property: Id(0)] string Location) : IEventPayload;