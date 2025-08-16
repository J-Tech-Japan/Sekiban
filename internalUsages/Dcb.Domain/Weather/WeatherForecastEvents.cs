using Sekiban.Dcb.Events;

namespace Dcb.Domain.Weather;

public record WeatherForecastCreated(
    Guid ForecastId,
    string Location,
    DateOnly Date,
    int TemperatureC,
    string? Summary) : IEventPayload;

public record WeatherForecastUpdated(
    Guid ForecastId,
    string Location,
    DateOnly Date,
    int TemperatureC,
    string? Summary) : IEventPayload;

public record WeatherForecastDeleted(Guid ForecastId) : IEventPayload;