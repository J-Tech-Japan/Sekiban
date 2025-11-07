using Dcb.Domain.Projections;
using ResultBoxes;
using Sekiban.Dcb.Queries;
namespace Dcb.Domain.Queries;

public record GetWeatherForecastListQuery :
    IMultiProjectionListQuery<WeatherForecastProjection, GetWeatherForecastListQuery, WeatherForecastItem>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    public bool IncludeDeleted { get; init; } = false;

    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }

    // Required static methods for IMultiProjectionListQuery
    public static ResultBox<IEnumerable<WeatherForecastItem>> HandleFilter( 
        WeatherForecastProjection projector,
        GetWeatherForecastListQuery query,
        IQueryContext context)
    {
        // Always use GetCurrentForecasts() for queries
        var forecasts = projector.GetCurrentForecasts();

        return ResultBox.FromValue(forecasts.Values.AsEnumerable());
    }

    public static ResultBox<IEnumerable<WeatherForecastItem>> HandleSort(
        IEnumerable<WeatherForecastItem> filteredList,
        GetWeatherForecastListQuery query,
        IQueryContext context)
    {
        return ResultBox.FromValue(filteredList.OrderByDescending(f => f.Date).AsEnumerable());
    }

    public string? WaitForSortableUniqueId { get; init; }
}
