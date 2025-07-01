using Orleans;
using Sekiban.Pure.Aggregates;

namespace DaprSample.Domain.Aggregates.WeatherForecasts.Payloads;

[GenerateSerializer]
public record DeletedWeatherForecast : IAggregatePayload
{
    public static DeletedWeatherForecast Instance => new();
}