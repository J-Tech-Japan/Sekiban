using Dcb.Domain.WithoutResult.Projections;
using Dcb.Domain.WithoutResult.Weather;
using Orleans;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;

namespace Dcb.Domain.WithoutResult.Queries;

[GenerateSerializer]
public record GetWeatherForecastListGenericQuery :
    IMultiProjectionListQuery<GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>, GetWeatherForecastListGenericQuery, WeatherForecastItem>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    [Id(0)]
    public bool IncludeDeleted { get; init; } = false;

    // Paging parameters
    [Id(1)] public int? PageNumber { get; init; }
    [Id(2)] public int? PageSize { get; init; }

    public static IEnumerable<WeatherForecastItem> HandleFilter(
        GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag> projector,
        GetWeatherForecastListGenericQuery query,
        IQueryContext context)
    {
        var tagStates = projector.GetCurrentTagStates();
        return tagStates.Values
            .Select(ts => ts.Payload as WeatherForecastState)
            .Where(s => s != null && (!s!.IsDeleted || query.IncludeDeleted))
            .Select(s => new WeatherForecastItem(
                s!.ForecastId,
                s.Location,
                s.Date.ToDateTime(TimeOnly.MinValue),
                s.TemperatureC,
                s.Summary,
                DateTime.UtcNow)) // LastUpdated はここでは簡易に現在時刻
            .AsEnumerable();
    }

    public static IEnumerable<WeatherForecastItem> HandleSort(
        IEnumerable<WeatherForecastItem> filteredList,
        GetWeatherForecastListGenericQuery query,
        IQueryContext context) =>
        filteredList.OrderByDescending(f => f.Date);

    // Wait for sortable unique ID
    [Id(3)]
    public string? WaitForSortableUniqueId { get; init; }
}
