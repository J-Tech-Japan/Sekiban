using Dcb.Domain.Queries;
using Dcb.Domain.Weather;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;

namespace Dcb.Domain.Queries;

/// <summary>
/// Count query for GenericTagMultiProjector-based Weather
/// </summary>
public record GetWeatherForecastCountGenericQuery :
    IMultiProjectionQuery<GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>, GetWeatherForecastCountGenericQuery, WeatherForecastCountResult>,
    IWaitForSortableUniqueId
{
    public string? WaitForSortableUniqueId { get; init; }

    public static ResultBox<WeatherForecastCountResult> HandleQuery(
        GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag> projector,
        GetWeatherForecastCountGenericQuery query,
        IQueryContext context)
    {
        var safeVersion = context.SafeVersion ?? 0;
        var unsafeVersion = context.UnsafeVersion ?? safeVersion;
        var total = projector.GetCurrentTagStates().Count; // tag states equal number of forecasts
        return ResultBox.FromValue(new WeatherForecastCountResult(
            SafeVersion: safeVersion,
            UnsafeVersion: unsafeVersion,
            TotalCount: total
        ));
    }
}
