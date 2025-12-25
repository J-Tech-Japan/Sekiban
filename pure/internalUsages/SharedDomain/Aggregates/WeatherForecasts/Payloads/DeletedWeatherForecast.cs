using Sekiban.Pure.Aggregates;
namespace SharedDomain.Aggregates.WeatherForecasts.Payloads;

[GenerateSerializer]
public record DeletedWeatherForecast : IAggregatePayload
{
    public static DeletedWeatherForecast Instance => new();
}
