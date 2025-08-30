using Dcb.Domain.Projections;
using Orleans;
using ResultBoxes;
using Sekiban.Dcb.Queries;

namespace Dcb.Domain.Queries;

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

    public static ResultBox<WeatherForecastCountResult> HandleQuery(
        WeatherForecastProjectorWithTagStateProjector projector,
        GetWeatherForecastCountSingleQuery query,
        IQueryContext context)
    {
        var tagStates = projector.GetCurrentTagStates();
        var totalCount = tagStates.Count;
        var unsafeCount = tagStates.Keys.Count(id => projector.IsTagStateUnsafe(id));
        var safeCount = totalCount - unsafeCount;

        return ResultBox.FromValue(new WeatherForecastCountResult(
            TotalCount: totalCount,
            SafeCount: safeCount,
            UnsafeCount: unsafeCount,
            IsSafeState: unsafeCount == 0,
            LastProcessedEventId: string.Empty
        ));
    }
}

