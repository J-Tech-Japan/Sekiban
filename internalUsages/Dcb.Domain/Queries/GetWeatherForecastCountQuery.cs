using Dcb.Domain.Projections;
using Orleans;
using ResultBoxes;
using Sekiban.Dcb.Queries;

namespace Dcb.Domain.Queries;

/// <summary>
/// Query to get the total count of weather forecasts
/// </summary>
[GenerateSerializer]
public record GetWeatherForecastCountQuery : 
    IMultiProjectionQuery<WeatherForecastProjection, GetWeatherForecastCountQuery, WeatherForecastCountResult>,
    IWaitForSortableUniqueId
{
    [Id(0)]
    public string? WaitForSortableUniqueId { get; init; }

    public static ResultBox<WeatherForecastCountResult> HandleQuery(
        WeatherForecastProjection projector,
        GetWeatherForecastCountQuery query,
        IQueryContext context)
    {
        var totalCount = projector.Forecasts.Count;
        var unsafeCount = projector.UnsafeForecasts.Count;
        var safeCount = totalCount - unsafeCount;
        
        return ResultBox.FromValue(new WeatherForecastCountResult(
            TotalCount: totalCount,
            SafeCount: safeCount,
            UnsafeCount: unsafeCount,
            IsSafeState: unsafeCount == 0,  // If no unsafe forecasts, state is safe
            LastProcessedEventId: string.Empty,  // This info is not available in query context
            SafeVersion: safeCount
        ));
    }
}

/// <summary>
/// Result containing weather forecast counts
/// </summary>
[GenerateSerializer]
public record WeatherForecastCountResult(
    [property: Id(0)] int TotalCount,
    [property: Id(1)] int SafeCount,
    [property: Id(2)] int UnsafeCount,
    [property: Id(3)] bool IsSafeState,
    [property: Id(4)] string LastProcessedEventId,
    [property: Id(5)] int SafeVersion
);