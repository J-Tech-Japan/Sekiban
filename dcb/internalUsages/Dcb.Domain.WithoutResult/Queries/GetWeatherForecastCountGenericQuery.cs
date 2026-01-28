using Dcb.Domain.WithoutResult.Queries;
using Dcb.Domain.WithoutResult.Weather;
using Orleans;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;

namespace Dcb.Domain.WithoutResult.Queries;

/// <summary>
/// Count query for GenericTagMultiProjector-based Weather
/// </summary>
public record GetWeatherForecastCountGenericQuery :
    IMultiProjectionQuery<GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>, GetWeatherForecastCountGenericQuery, WeatherForecastCountResult>,
    IWaitForSortableUniqueId
{
    [Id(0)]
    public string? WaitForSortableUniqueId { get; init; }

    public static WeatherForecastCountResult HandleQuery(
        GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag> projector,
        GetWeatherForecastCountGenericQuery query,
        IQueryContext context)
    {
        var safeVersion = context.SafeVersion ?? 0;
        var unsafeVersion = context.UnsafeVersion ?? safeVersion;
        var total = projector.GetStatePayloads().Count(); // tag states equal number of forecasts
        return new WeatherForecastCountResult(
            SafeVersion: safeVersion,
            UnsafeVersion: unsafeVersion,
            TotalCount: total
        );
    }
}
