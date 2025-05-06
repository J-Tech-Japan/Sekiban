using Sekiban.Pure.Events;

namespace OrleansSekiban.Domain.Aggregates.WeatherForecasts.Events;

[GenerateSerializer]
public record WeatherForecastLocationUpdated(string NewLocation) : IEventPayload;
