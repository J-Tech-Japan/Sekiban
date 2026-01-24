using Sekiban.Dcb.Events;
namespace Dcb.Domain.WithoutResult.Weather;

public record LocationNameChanged(Guid ForecastId, string NewLocationName, string OldLocationName) : IEventPayload;
