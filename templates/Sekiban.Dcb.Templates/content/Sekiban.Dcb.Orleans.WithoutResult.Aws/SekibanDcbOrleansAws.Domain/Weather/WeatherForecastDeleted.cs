using Sekiban.Dcb.Events;
namespace Dcb.Domain.WithoutResult.Weather;

public record WeatherForecastDeleted(Guid ForecastId) : IEventPayload;
