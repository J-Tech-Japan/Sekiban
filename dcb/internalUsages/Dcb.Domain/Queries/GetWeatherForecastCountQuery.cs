using Dcb.Domain.Projections;
using ResultBoxes;
using Sekiban.Dcb.Queries;

namespace Dcb.Domain.Queries;

/// <summary>
/// Query to get the total count of weather forecasts
/// </summary>
public record GetWeatherForecastCountQuery :
    IMultiProjectionQuery<WeatherForecastProjection, GetWeatherForecastCountQuery, WeatherForecastCountResult>,
    IWaitForSortableUniqueId
{
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