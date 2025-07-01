using Orleans;
using DaprSample.Domain.Aggregates.WeatherForecasts.Payloads;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;

namespace DaprSample.Domain.Aggregates.WeatherForecasts.Queries;

[GenerateSerializer]
public record WeatherForecastQuery : IMultiProjectionListQuery<AggregateListProjector<WeatherForecastProjector>, WeatherForecastQuery, WeatherForecastResponse>, IWaitForSortableUniqueId
{
    public string? WaitForSortableUniqueId { get; set; }
    
    public static ResultBox<IEnumerable<WeatherForecastResponse>> HandleFilter(MultiProjectionState<AggregateListProjector<WeatherForecastProjector>> projection, WeatherForecastQuery query, IQueryContext context)
    {
        return projection.Payload.Aggregates
            .Where(m => m.Value.GetPayload() is WeatherForecast)
            .Select(m => ((WeatherForecast)m.Value.GetPayload(), m.Value.PartitionKeys))
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
    [property: Id(3)] int TemperatureC,
    [property: Id(4)] string? Summary,
    [property: Id(5)] int TemperatureF
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
            32 + (int)(forecast.TemperatureC.Value / 0.5556)
        );
    }
}