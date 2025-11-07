using Dcb.Domain.Projections;
using Dcb.Domain.Weather;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;

namespace Dcb.Domain.Queries;

public record GetWeatherForecastListGenericQuery :
    IMultiProjectionListQuery<GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>, GetWeatherForecastListGenericQuery, WeatherForecastItem>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    public bool IncludeDeleted { get; init; } = false;

    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }

    public static ResultBox<IEnumerable<WeatherForecastItem>> HandleFilter(
        GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag> projector,
        GetWeatherForecastListGenericQuery query,
        IQueryContext context)
    {
        var tagStates = projector.GetCurrentTagStates();
        var items = tagStates.Values
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

        return ResultBox.FromValue(items);
    }

    public static ResultBox<IEnumerable<WeatherForecastItem>> HandleSort(
        IEnumerable<WeatherForecastItem> filteredList,
        GetWeatherForecastListGenericQuery query,
        IQueryContext context)
    {
        return ResultBox.FromValue(filteredList.OrderByDescending(f => f.Date).AsEnumerable());
    }

    public string? WaitForSortableUniqueId { get; init; }
}

