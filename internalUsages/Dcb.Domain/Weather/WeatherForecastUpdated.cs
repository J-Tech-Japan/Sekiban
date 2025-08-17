using Sekiban.Dcb.Events;
namespace Dcb.Domain.Weather;

public record WeatherForecastUpdated(
    Guid ForecastId,
    string Location,
    DateOnly Date,
    int TemperatureC,
    string? Summary) : IEventPayload;