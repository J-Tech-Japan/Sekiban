using Dcb.Domain.WithoutResult.Projections;
using Orleans;
using Sekiban.Dcb.Queries;

namespace Dcb.Domain.WithoutResult.Queries;

/// <summary>
/// Query to get the total count of weather forecasts
/// </summary>
[GenerateSerializer]
public record GetWeatherForecastCountQuery : 
    IMultiProjectionQueryWithoutResult<WeatherForecastProjection, GetWeatherForecastCountQuery, WeatherForecastCountResult>,
    IWaitForSortableUniqueId
{
    [Id(0)]
    public string? WaitForSortableUniqueId { get; init; }

    public static WeatherForecastCountResult HandleQuery(
        WeatherForecastProjection projector,
        GetWeatherForecastCountQuery query,
        IQueryContext context)
    {
        var safeVersion = context.SafeVersion ?? 0;
        var unsafeVersion = context.UnsafeVersion ?? safeVersion;
        var total = projector.Forecasts.Count;
        return new WeatherForecastCountResult(
            SafeVersion: safeVersion,
            UnsafeVersion: unsafeVersion,
            TotalCount: total
        );
    }
}
