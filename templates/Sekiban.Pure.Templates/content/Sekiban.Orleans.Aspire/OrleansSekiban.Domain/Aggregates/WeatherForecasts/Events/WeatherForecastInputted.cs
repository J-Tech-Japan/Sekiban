using OrleansSekiban.Domain.ValueObjects;
using Sekiban.Pure.Events;

namespace OrleansSekiban.Domain.Aggregates.WeatherForecasts.Events;

[GenerateSerializer]
public record WeatherForecastInputted(
    string Location,
    DateOnly Date,
    TemperatureCelsius TemperatureC,
    string Summary
) : IEventPayload;
