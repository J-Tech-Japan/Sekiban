using Dcb.Domain.WithoutResult.Projections;
using Orleans;
using Sekiban.Dcb.Queries;

namespace Dcb.Domain.WithoutResult.Queries;

/// <summary>
/// Count query for the TagState-based projector
/// </summary>
[GenerateSerializer]
public record GetWeatherForecastCountSingleQuery :
    IMultiProjectionQueryWithoutResult<WeatherForecastProjectorWithTagStateProjector, GetWeatherForecastCountSingleQuery, WeatherForecastCountResult>,
    IWaitForSortableUniqueId
{
    [Id(0)]
    public string? WaitForSortableUniqueId { get; init; }

    public static WeatherForecastCountResult HandleQuery(
        WeatherForecastProjectorWithTagStateProjector projector,
        GetWeatherForecastCountSingleQuery query,
        IQueryContext context)
    {
        var safeVersion = context.SafeVersion ?? 0;
        var unsafeVersion = context.UnsafeVersion ?? safeVersion;
        var total = projector.GetCurrentTagStates().Count;
        return new WeatherForecastCountResult(
            SafeVersion: safeVersion,
            UnsafeVersion: unsafeVersion,
            TotalCount: total
        );
    }
}
