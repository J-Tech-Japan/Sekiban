using Orleans;
using Sekiban.Pure.Aggregates;

namespace DaprSekiban.Domain.Aggregates.WeatherForecasts.Payloads;

[GenerateSerializer]
public record DeletedWeatherForecast : IAggregatePayload
{
    public static DeletedWeatherForecast Instance => new();
}