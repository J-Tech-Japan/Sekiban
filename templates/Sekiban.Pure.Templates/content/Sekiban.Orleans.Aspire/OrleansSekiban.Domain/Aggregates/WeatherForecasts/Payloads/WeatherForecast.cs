using OrleansSekiban.Domain.ValueObjects;
using Sekiban.Pure.Aggregates;

namespace OrleansSekiban.Domain;

[GenerateSerializer]
public record WeatherForecast(
    string Location,
    DateOnly Date,
    TemperatureCelsius TemperatureC,
    string Summary
) : IAggregatePayload;
