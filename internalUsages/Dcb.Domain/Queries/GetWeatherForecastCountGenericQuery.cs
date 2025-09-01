using Dcb.Domain.Queries;
using Dcb.Domain.Weather;
using Orleans;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;

namespace Dcb.Domain.Queries;

/// <summary>
/// Count query for GenericTagMultiProjector-based Weather
/// </summary>
[GenerateSerializer]
public record GetWeatherForecastCountGenericQuery :
    IMultiProjectionQuery<GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>, GetWeatherForecastCountGenericQuery, WeatherForecastCountResult>,
    IWaitForSortableUniqueId
{
    [Id(0)]
    public string? WaitForSortableUniqueId { get; init; }

    public static ResultBox<WeatherForecastCountResult> HandleQuery(
        GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag> projector,
        GetWeatherForecastCountGenericQuery query,
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
            LastProcessedEventId: string.Empty,
            SafeVersion: safeCount
        ));
    }
}

