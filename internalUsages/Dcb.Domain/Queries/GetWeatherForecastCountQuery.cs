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
        var safeVersion = context.SafeVersion ?? 0;
        var unsafeVersion = context.UnsafeVersion ?? safeVersion;
        var total = projector.Forecasts.Count;
        return ResultBox.FromValue(new WeatherForecastCountResult(
            SafeVersion: safeVersion,
            UnsafeVersion: unsafeVersion,
            TotalCount: total
        ));
    }
}

/// <summary>
/// Result containing weather forecast counts
/// </summary>
[GenerateSerializer]
public record WeatherForecastCountResult(
    [property: Id(0)] int SafeVersion,
    [property: Id(1)] int UnsafeVersion,
    [property: Id(2)] int TotalCount
);