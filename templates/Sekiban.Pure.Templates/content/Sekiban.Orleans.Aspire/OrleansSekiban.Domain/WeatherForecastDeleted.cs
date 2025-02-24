using Orleans;
using Sekiban.Pure.Events;

namespace OrleansSekiban.Domain;

[GenerateSerializer]
public record WeatherForecastDeleted() : IEventPayload;
