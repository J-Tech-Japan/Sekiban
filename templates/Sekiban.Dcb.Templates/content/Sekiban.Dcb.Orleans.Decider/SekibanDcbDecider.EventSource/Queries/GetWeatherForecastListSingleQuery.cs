using Dcb.EventSource.Projections;
using Dcb.EventSource.Weather;
using Dcb.ImmutableModels.States.Weather;
using Orleans;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;

namespace Dcb.EventSource.Queries;

[GenerateSerializer]
public record GetWeatherForecastListSingleQuery :
    IMultiProjectionListQuery<WeatherForecastProjectorWithTagStateProjector, GetWeatherForecastListSingleQuery, WeatherForecastItem>,
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
    public static IEnumerable<WeatherForecastItem> HandleFilter(
        WeatherForecastProjectorWithTagStateProjector projector,
        GetWeatherForecastListSingleQuery query,
        IQueryContext context)
    {
        // Projector stores TagState whose payload is WeatherForecastState
        var tagStates = projector.GetCurrentTagStates();

        return tagStates
            .Values
            .Select(ts => new { Tag = ts })
            .Select(x => new { State = x.Tag.Payload as WeatherForecastState, x.Tag.LastSortedUniqueId })
            .Where(x => x.State != null && (!x.State!.IsDeleted || query.IncludeDeleted))
            .Select(x => new WeatherForecastItem(
                x.State!.ForecastId,
                x.State.Location,
                x.State.Date.ToDateTime(TimeOnly.MinValue),
                x.State.TemperatureC,
                x.State.Summary,
                ParseLastUpdated(x.LastSortedUniqueId)))
            .AsEnumerable();
    }

    public static IEnumerable<WeatherForecastItem> HandleSort(
        IEnumerable<WeatherForecastItem> filteredList,
        GetWeatherForecastListSingleQuery query,
        IQueryContext context) =>
        filteredList.OrderByDescending(f => f.Date);

    private static DateTime ParseLastUpdated(string sortableId)
    {
        if (string.IsNullOrEmpty(sortableId)) return DateTime.MinValue;
        try { return new SortableUniqueId(sortableId).GetDateTime(); }
        catch { return DateTime.MinValue; }
    }

    // Wait for sortable unique ID (from IWaitForSortableUniqueId)
    [Id(3)]
    public string? WaitForSortableUniqueId { get; init; }
}
