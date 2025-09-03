using Sekiban.Dcb.Events;
namespace Dcb.Domain.Weather;

public record WeatherForecastDeleted(Guid ForecastId) : IEventPayload;
