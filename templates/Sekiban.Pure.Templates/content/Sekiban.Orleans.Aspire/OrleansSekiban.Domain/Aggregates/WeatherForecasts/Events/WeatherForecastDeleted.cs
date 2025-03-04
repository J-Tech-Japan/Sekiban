using Sekiban.Pure.Events;

namespace OrleansSekiban.Domain.Aggregates.WeatherForecasts.Events;

[GenerateSerializer]
public record WeatherForecastDeleted() : IEventPayload;
