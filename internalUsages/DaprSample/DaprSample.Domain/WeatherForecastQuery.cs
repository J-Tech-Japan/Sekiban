using Orleans;
using DaprSample.Domain.Aggregates.WeatherForecasts.Payloads;
using Sekiban.Pure.Query;
using Sekiban.Pure.Query.MultiProjections;

namespace DaprSample.Domain;

[GenerateSerializer]
public record WeatherForecastQuery : IMultiProjectionListQuery<SimpleMultiProjection<WeatherForecast>, Handler, WeatherForecastQuery.Record>, IWaitForSortableUniqueId
{
    public string? WaitForSortableUniqueId { get; set; }

    [GenerateSerializer]
    public record Record([property: Id(0)] Guid AggregateId, [property: Id(1)] WeatherForecast Forecast);

    [GenerateSerializer]
    public class Handler : IMultiProjectionQueryHandler<SimpleMultiProjection<WeatherForecast>, Record>
    {
        public static IEnumerable<Record> HandleFilter(MultiProjectionState<SimpleMultiProjection<WeatherForecast>> projection, WeatherForecastQuery query)
        {
            var list = projection.ProjectionValues
                .Select(kv => new Record(kv.Key, kv.Value as WeatherForecast ?? WeatherForecast.Empty))
                .Where(r => r.Forecast is not null);
            return list;
        }

        public static IEnumerable<Record> HandleSort(IEnumerable<Record> filteredList, WeatherForecastQuery query) =>
            filteredList.OrderBy(r => r.Forecast.Date).ThenBy(r => r.Forecast.Location);
    }
}