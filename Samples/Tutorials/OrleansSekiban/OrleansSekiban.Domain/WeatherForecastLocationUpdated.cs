using Sekiban.Pure.Events;

namespace OrleansSekiban.Domain;

[GenerateSerializer]
public record WeatherForecastLocationUpdated(string NewLocation) : IEventPayload;
