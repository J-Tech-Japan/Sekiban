using Orleans;
using DaprSekiban.Domain.Aggregates.WeatherForecasts.Payloads;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;

namespace DaprSekiban.Domain.Aggregates.WeatherForecasts.Queries;

public record WeatherForecastQuery(string LocationContains) : IMultiProjectionListQuery<AggregateListProjector<WeatherForecastProjector>, WeatherForecastQuery, WeatherForecastResponse>, IWaitForSortableUniqueId
{
    public string? WaitForSortableUniqueId { get; set; }
    
    public static ResultBox<IEnumerable<WeatherForecastResponse>> HandleFilter(MultiProjectionState<AggregateListProjector<WeatherForecastProjector>> projection, WeatherForecastQuery query, IQueryContext context)
    {
        return projection.Payload.Aggregates
            .Where(m => m.Value.GetPayload() is WeatherForecast)
            .Select(m => ((WeatherForecast)m.Value.GetPayload(), m.Value.PartitionKeys))
            .Where(tuple => string.IsNullOrEmpty(query.LocationContains) ||
                            tuple.Item1.Location.Contains(query.LocationContains, StringComparison.OrdinalIgnoreCase))
            .Select(tuple => WeatherForecastResponse.FromWeatherForecast(tuple.PartitionKeys.AggregateId, tuple.Item1))
            .ToResultBox();
    }

    public static ResultBox<IEnumerable<WeatherForecastResponse>> HandleSort(IEnumerable<WeatherForecastResponse> filteredList, WeatherForecastQuery query, IQueryContext context)
    {
        return filteredList.OrderBy(m => m.Date).AsEnumerable().ToResultBox();
    }
}

[GenerateSerializer]
public record WeatherForecastResponse(
    [property: Id(0)] Guid WeatherForecastId,
    [property: Id(1)] string Location,
    [property: Id(2)] DateOnly Date,
    [property: Id(3)] double TemperatureC,
    [property: Id(4)] string? Summary,
    [property: Id(5)] double TemperatureF
)
{
    public static WeatherForecastResponse FromWeatherForecast(Guid id, WeatherForecast forecast)
    {
        return new WeatherForecastResponse(
            id,
            forecast.Location,
            forecast.Date,
            forecast.TemperatureC.Value,
            forecast.Summary,
            forecast.TemperatureC.GetFahrenheit()
        );
    }
}