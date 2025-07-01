using Orleans;
using DaprSample.Domain.ValueObjects;
using Sekiban.Pure.Aggregates;

namespace DaprSample.Domain.Aggregates.WeatherForecasts.Payloads;

[GenerateSerializer]
public record WeatherForecast : IAggregatePayload
{
    [Id(0)] public string Location { get; init; } = string.Empty;
    [Id(1)] public DateOnly Date { get; init; }
    [Id(2)] public TemperatureCelsius TemperatureC { get; init; } = new(0);
    [Id(3)] public string? Summary { get; init; }

    public static WeatherForecast Empty => new();
}