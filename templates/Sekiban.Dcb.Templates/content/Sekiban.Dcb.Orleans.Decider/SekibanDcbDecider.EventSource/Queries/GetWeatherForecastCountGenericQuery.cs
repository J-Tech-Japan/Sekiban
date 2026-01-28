using Dcb.EventSource.Projections;
using Orleans;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;

namespace Dcb.EventSource.Queries;

/// <summary>
/// Count query for WeatherForecastProjection-based Weather
/// </summary>
public record GetWeatherForecastCountGenericQuery :
    IMultiProjectionQuery<WeatherForecastProjection, GetWeatherForecastCountGenericQuery, WeatherForecastCountResult>,
    IWaitForSortableUniqueId
{
    [Id(0)]
    public string? WaitForSortableUniqueId { get; init; }

    public static WeatherForecastCountResult HandleQuery(
        WeatherForecastProjection projector,
        GetWeatherForecastCountGenericQuery query,
        IQueryContext context)
    {
        var safeVersion = context.SafeVersion ?? 0;
        var unsafeVersion = context.UnsafeVersion ?? safeVersion;
        var total = projector.GetCurrentForecasts().Count;
        return new WeatherForecastCountResult(
            SafeVersion: safeVersion,
            UnsafeVersion: unsafeVersion,
            TotalCount: total
        );
    }
}
