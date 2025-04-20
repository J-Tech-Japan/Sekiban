using OrleansSekiban.Domain.ValueObjects;
using Sekiban.Pure.Aggregates;
namespace OrleansSekiban.Domain.Aggregates.WeatherForecasts.Payloads;

[GenerateSerializer]
public record DeletedWeatherForecast(
    string Location,
    DateOnly Date,
    TemperatureCelsius TemperatureC,
    string Summary
) : IAggregatePayload;
