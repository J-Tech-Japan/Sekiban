using Sekiban.Dcb.Events;

namespace Dcb.Domain.Weather;

public record WeatherForecastCreated(
    Guid ForecastId,
    string Location,
    DateOnly Date,
    int TemperatureC,
    string? Summary) : IEventPayload;