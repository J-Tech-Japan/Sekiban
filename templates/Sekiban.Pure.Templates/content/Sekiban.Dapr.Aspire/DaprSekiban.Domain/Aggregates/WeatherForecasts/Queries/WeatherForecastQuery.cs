using Orleans;
using DaprSekiban.Domain.Aggregates.WeatherForecasts.Payloads;
using DaprSekiban.Domain.ValueObjects;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;

namespace DaprSekiban.Domain.Aggregates.WeatherForecasts.Queries;

public record WeatherForecastQuery(string LocationContains) : IMultiProjectionListQuery<AggregateListProjector<WeatherForecastProjector>, WeatherForecastQuery, WeatherForecastResponse>, IWaitForSortableUniqueId, IQueryPagingParameterCommon
{
    public string? WaitForSortableUniqueId { get; set; }
    public int? PageSize { get; init; } = null;
    public int? PageNumber { get; init; } = null;
    public string? SortBy { get; init; } = null;
    public bool IsAsc { get; init; } = false;
    
    public static ResultBox<IEnumerable<WeatherForecastResponse>> HandleFilter(MultiProjectionState<AggregateListProjector<WeatherForecastProjector>> projection, WeatherForecastQuery query, IQueryContext context)
    {
        return projection.Payload.Aggregates
            .Where(m => m.Value.GetPayload() is WeatherForecast)
            .Select(m => ((WeatherForecast)m.Value.GetPayload(), m.Value.PartitionKeys, new SortableUniqueIdValue(m.Value.LastSortableUniqueId)))
            .Where(tuple => string.IsNullOrEmpty(query.LocationContains) ||
                            tuple.Item1.Location.Contains(query.LocationContains, StringComparison.OrdinalIgnoreCase))
            .Select(tuple => WeatherForecastResponse.FromWeatherForecast(tuple.PartitionKeys.AggregateId, tuple.Item1, tuple.Item3.GetTicks()))
            .ToResultBox();
    }

    public static ResultBox<IEnumerable<WeatherForecastResponse>> HandleSort(IEnumerable<WeatherForecastResponse> filteredList, WeatherForecastQuery query, IQueryContext context)
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
                    ? sortedList.OrderBy(m => m.TemperatureC).ThenByDescending(m => m.UpdatedAt)
                    : sortedList.OrderByDescending(m => m.TemperatureC).ThenByDescending(m => m.UpdatedAt),
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
}

[GenerateSerializer]
public record WeatherForecastResponse(
    [property: Id(0)] Guid WeatherForecastId,
    [property: Id(1)] string Location,
    [property: Id(2)] DateOnly Date,
    [property: Id(3)] double TemperatureC,
    [property: Id(4)] string? Summary,
    [property: Id(5)] double TemperatureF,
    [property: Id(6)] DateTime UpdatedAt
)
{
    public static WeatherForecastResponse FromWeatherForecast(Guid id, WeatherForecast forecast, DateTime updatedAt)
    {
        return new WeatherForecastResponse(
            id,
            forecast.Location,
            forecast.Date,
            forecast.TemperatureC.Value,
            forecast.Summary,
            forecast.TemperatureC.GetFahrenheit(),
            updatedAt
        );
    }
}