using Dcb.Domain.Projections;
using ResultBoxes;
using Sekiban.Dcb.Queries;

namespace Dcb.Domain.Queries;

/// <summary>
/// Count query for the TagState-based projector
/// </summary>
public record GetWeatherForecastCountSingleQuery :
    IMultiProjectionQuery<WeatherForecastProjectorWithTagStateProjector, GetWeatherForecastCountSingleQuery, WeatherForecastCountResult>,
    IWaitForSortableUniqueId
{
    public string? WaitForSortableUniqueId { get; init; }

    public static ResultBox<WeatherForecastCountResult> HandleQuery(
        WeatherForecastProjectorWithTagStateProjector projector,
        GetWeatherForecastCountSingleQuery query,
        IQueryContext context)
    {
        var safeVersion = context.SafeVersion ?? 0;
        var unsafeVersion = context.UnsafeVersion ?? safeVersion;
    var total = projector.GetCurrentTagStates().Count;
        return ResultBox.FromValue(new WeatherForecastCountResult(
            SafeVersion: safeVersion,
            UnsafeVersion: unsafeVersion,
            TotalCount: total
        ));
    }
}
