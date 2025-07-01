using Orleans;
using DaprSample.Domain.ValueObjects;
using Sekiban.Pure.Events;

namespace DaprSample.Domain.Aggregates.WeatherForecasts.Events;

[GenerateSerializer]
public record WeatherForecastInputted(
    [property: Id(0)] string Location,
    [property: Id(1)] DateOnly Date,
    [property: Id(2)] TemperatureCelsius TemperatureC,
    [property: Id(3)] string? Summary) : IEventPayload;