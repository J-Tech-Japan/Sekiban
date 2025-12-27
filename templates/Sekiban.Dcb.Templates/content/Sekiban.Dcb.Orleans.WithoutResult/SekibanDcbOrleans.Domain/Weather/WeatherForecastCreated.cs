using Sekiban.Dcb.Events;
namespace Dcb.Domain.WithoutResult.Weather;

public record WeatherForecastCreated(Guid ForecastId, string Location, DateOnly Date, int TemperatureC, string? Summary)
    : IEventPayload;
