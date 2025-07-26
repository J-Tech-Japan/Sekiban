using OrleansSekiban.Domain.Aggregates.WeatherForecasts.Payloads;
using OrleansSekiban.Domain.ValueObjects;
using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;

namespace OrleansSekiban.Domain.Aggregates.WeatherForecasts.Queries;

[GenerateSerializer(GenerateFieldIds = GenerateFieldIds.PublicProperties)]
public record WeatherForecastQuery(string LocationContains)
    : IMultiProjectionListQuery<AggregateListProjector<WeatherForecastProjector>, WeatherForecastQuery,
            WeatherForecastQuery.WeatherForecastRecord>,
        IWaitForSortableUniqueId, IQueryPagingParameterCommon
{
    public static ResultBox<IEnumerable<WeatherForecastRecord>> HandleFilter(
        MultiProjectionState<AggregateListProjector<WeatherForecastProjector>> projection, WeatherForecastQuery query,
        IQueryContext context)
    {
        return projection.Payload.Aggregates.Where(m => m.Value.GetPayload() is WeatherForecast)
            .Select(m => ((WeatherForecast)m.Value.GetPayload(), m.Value.PartitionKeys, new SortableUniqueIdValue(m.Value.LastSortableUniqueId)))
            .Where(tuple => string.IsNullOrEmpty(query.LocationContains) ||
                            tuple.Item1.Location.Contains(query.LocationContains, StringComparison.OrdinalIgnoreCase))
            .Select(tuple => new WeatherForecastRecord(tuple.PartitionKeys.AggregateId, tuple.Item1.Location,
                tuple.Item1.Date, tuple.Item1.TemperatureC, tuple.Item1.Summary,
                tuple.Item1.TemperatureC.GetFahrenheit(), tuple.Item3.GetTicks()))
            .ToResultBox();
    }

    public static ResultBox<IEnumerable<WeatherForecastRecord>> HandleSort(
        IEnumerable<WeatherForecastRecord> filteredList, WeatherForecastQuery query, IQueryContext context)
    {
        var sortedList = filteredList;

        // Apply primary sort if SortBy is specified and matches a property
        if (!string.IsNullOrEmpty(query.SortBy))
        {
            sortedList = query.SortBy.ToLower() switch
            {
                "weatherforecastid" => query.IsAsc 
                    ? sortedList.OrderBy(m => m.WeatherForecastId).ThenByDescending(m => m.UpdatedAt)
                    : sortedList.OrderByDescending(m => m.WeatherForecastId).ThenByDescending(m => m.UpdatedAt),
                "location" => query.IsAsc 
                    ? sortedList.OrderBy(m => m.Location).ThenByDescending(m => m.UpdatedAt)
                    : sortedList.OrderByDescending(m => m.Location).ThenByDescending(m => m.UpdatedAt),
                "date" => query.IsAsc 
                    ? sortedList.OrderBy(m => m.Date).ThenByDescending(m => m.UpdatedAt)
                    : sortedList.OrderByDescending(m => m.Date).ThenByDescending(m => m.UpdatedAt),
                "temperaturec" => query.IsAsc 
                    ? sortedList.OrderBy(m => m.TemperatureC.Value).ThenByDescending(m => m.UpdatedAt)
                    : sortedList.OrderByDescending(m => m.TemperatureC.Value).ThenByDescending(m => m.UpdatedAt),
                "summary" => query.IsAsc 
                    ? sortedList.OrderBy(m => m.Summary).ThenByDescending(m => m.UpdatedAt)
                    : sortedList.OrderByDescending(m => m.Summary).ThenByDescending(m => m.UpdatedAt),
                "temperaturef" => query.IsAsc 
                    ? sortedList.OrderBy(m => m.TemperatureF).ThenByDescending(m => m.UpdatedAt)
                    : sortedList.OrderByDescending(m => m.TemperatureF).ThenByDescending(m => m.UpdatedAt),
                "updatedat" => query.IsAsc 
                    ? sortedList.OrderBy(m => m.UpdatedAt)
                    : sortedList.OrderByDescending(m => m.UpdatedAt),
                _ => sortedList.OrderByDescending(m => m.UpdatedAt) // Default fallback
            };
        }
        else
        {
            // Default sort by UpdatedAt DESC if no SortBy specified
            sortedList = sortedList.OrderByDescending(m => m.UpdatedAt);
        }

        return sortedList.AsEnumerable().ToResultBox();
    }

    [GenerateSerializer]
    public record WeatherForecastRecord(
        Guid WeatherForecastId,
        string Location,
        DateOnly Date,
        TemperatureCelsius TemperatureC,
        string Summary,
        double TemperatureF,
        DateTime UpdatedAt
    );

    public string? WaitForSortableUniqueId { get; set; }
    public int? PageSize { get; init; } = null;
    public int? PageNumber { get; init; } = null;
    public string? SortBy { get; init; } = null;
    public bool IsAsc { get; init; } = false;
}