using Dcb.Domain.Projections;
using Orleans;
using ResultBoxes;
using Sekiban.Dcb.Queries;
namespace Dcb.Domain.Queries;

[GenerateSerializer]
public record GetWeatherForecastListQuery :
    IMultiProjectionListQuery<WeatherForecastProjection, GetWeatherForecastListQuery, WeatherForecastItem>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    [Id(0)]
    public bool IncludeDeleted { get; init; } = false;

    // Paging parameters (from IQueryPagingParameter)
    [Id(1)]
    public int? PageNumber { get; init; }
    [Id(2)]
    public int? PageSize { get; init; }

    // Required static methods for IMultiProjectionListQuery
    public static ResultBox<IEnumerable<WeatherForecastItem>> HandleFilter(
        WeatherForecastProjection projector,
        GetWeatherForecastListQuery query,
        IQueryContext context)
    {
        // Always use GetCurrentForecasts() for queries (includes unsafe state)
        // GetSafeForecasts() should only be used for special cases requiring guaranteed consistency
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

    // Wait for sortable unique ID (from IWaitForSortableUniqueId)
    [Id(3)]
    public string? WaitForSortableUniqueId { get; init; }
}
