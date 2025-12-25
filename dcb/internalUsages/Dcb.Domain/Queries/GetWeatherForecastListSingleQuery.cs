using Dcb.Domain.Projections;
using Dcb.Domain.Weather;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;

namespace Dcb.Domain.Queries;

public record GetWeatherForecastListSingleQuery :
    IMultiProjectionListQuery<WeatherForecastProjectorWithTagStateProjector, GetWeatherForecastListSingleQuery, WeatherForecastItem>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    public bool IncludeDeleted { get; init; } = false;

    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }

    // Required static methods for IMultiProjectionListQuery
    public static ResultBox<IEnumerable<WeatherForecastItem>> HandleFilter(
        WeatherForecastProjectorWithTagStateProjector projector,
        GetWeatherForecastListSingleQuery query,
        IQueryContext context)
    {
        // Projector stores TagState whose payload is WeatherForecastState
        var tagStates = projector.GetCurrentTagStates();

        var items = tagStates
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

        return ResultBox.FromValue(items);
    }

    public static ResultBox<IEnumerable<WeatherForecastItem>> HandleSort(
        IEnumerable<WeatherForecastItem> filteredList,
        GetWeatherForecastListSingleQuery query,
        IQueryContext context)
    {
        return ResultBox.FromValue(filteredList.OrderByDescending(f => f.Date).AsEnumerable());
    }

    private static DateTime ParseLastUpdated(string sortableId)
    {
        if (string.IsNullOrEmpty(sortableId)) return DateTime.MinValue;
        try { return new SortableUniqueId(sortableId).GetDateTime(); }
        catch { return DateTime.MinValue; }
    }

    public string? WaitForSortableUniqueId { get; init; }
}
