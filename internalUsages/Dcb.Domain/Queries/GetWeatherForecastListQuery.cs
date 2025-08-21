using Dcb.Domain.Projections;
using ResultBoxes;
using Sekiban.Dcb.Queries;
namespace Dcb.Domain.Queries;

[global::Orleans.GenerateSerializer]
public record GetWeatherForecastListQuery : IMultiProjectionListQuery<WeatherForecastProjection, GetWeatherForecastListQuery, WeatherForecastItem>
{
    [global::Orleans.Id(0)]
    public bool IncludeDeleted { get; init; } = false;
    
    // Paging parameters (from IQueryPagingParameter)
    [global::Orleans.Id(1)]
    public int? PageNumber { get; init; }
    [global::Orleans.Id(2)]
    public int? PageSize { get; init; }

    // Required static methods for IMultiProjectionListQuery
    public static ResultBox<IEnumerable<WeatherForecastItem>> HandleFilter(
        WeatherForecastProjection projector,
        GetWeatherForecastListQuery query,
        IQueryContext context)
    {
        var forecasts = query.IncludeDeleted 
            ? projector.GetCurrentForecasts()
            : projector.GetSafeForecasts();
            
        return ResultBox.FromValue(forecasts.Values.AsEnumerable());
    }

    public static ResultBox<IEnumerable<WeatherForecastItem>> HandleSort(
        IEnumerable<WeatherForecastItem> filteredList,
        GetWeatherForecastListQuery query,
        IQueryContext context)
    {
        return ResultBox.FromValue(filteredList.OrderByDescending(f => f.Date).AsEnumerable());
    }
}