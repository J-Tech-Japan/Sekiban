using Dcb.EventSource.Projections;
using Orleans;
using Sekiban.Dcb.Queries;

namespace Dcb.EventSource.Queries;

/// <summary>
/// Count query for the TagState-based projector
/// </summary>
[GenerateSerializer]
public record GetWeatherForecastCountSingleQuery :
    IMultiProjectionQuery<WeatherForecastProjectorWithTagStateProjector, GetWeatherForecastCountSingleQuery, WeatherForecastCountResult>,
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
