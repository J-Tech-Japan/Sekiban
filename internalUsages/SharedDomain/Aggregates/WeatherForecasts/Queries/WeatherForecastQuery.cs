using ResultBoxes;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
using SharedDomain.Aggregates.WeatherForecasts.Payloads;
namespace SharedDomain.Aggregates.WeatherForecasts.Queries;

[GenerateSerializer(GenerateFieldIds = GenerateFieldIds.PublicProperties)]
public record WeatherForecastQuery :
    IMultiProjectionListQuery<AggregateListProjector<WeatherForecastProjector>, WeatherForecastQuery,
        WeatherForecastResponse>,
    IWaitForSortableUniqueId
{

    public static ResultBox<IEnumerable<WeatherForecastResponse>> HandleFilter(
        MultiProjectionState<AggregateListProjector<WeatherForecastProjector>> projection,
        WeatherForecastQuery query,
        IQueryContext context)
    {
        return projection
            .Payload
            .Aggregates
            .Where(m => m.Value.GetPayload() is WeatherForecast)
            .Select(m => ((WeatherForecast)m.Value.GetPayload(), m.Value.PartitionKeys))
            .Select(tuple => WeatherForecastResponse.FromWeatherForecast(tuple.PartitionKeys.AggregateId, tuple.Item1))
            .ToResultBox();
    }

    public static ResultBox<IEnumerable<WeatherForecastResponse>> HandleSort(
        IEnumerable<WeatherForecastResponse> filteredList,
        WeatherForecastQuery query,
        IQueryContext context)
    {
        return filteredList.OrderBy(m => m.Date).AsEnumerable().ToResultBox();
    }
    public string? WaitForSortableUniqueId { get; set; }
}
