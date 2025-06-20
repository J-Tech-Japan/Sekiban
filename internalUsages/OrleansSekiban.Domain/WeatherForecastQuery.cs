using OrleansSekiban.Domain.Aggregates.WeatherForecasts;
using OrleansSekiban.Domain.Aggregates.WeatherForecasts.Payloads;
using OrleansSekiban.Domain.ValueObjects;
using ResultBoxes;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;

namespace OrleansSekiban.Domain;

[GenerateSerializer]
public record WeatherForecastQuery(string LocationContains)
    : IMultiProjectionListQuery<AggregateListProjector<WeatherForecastProjector>, WeatherForecastQuery, WeatherForecastQuery.WeatherForecastRecord>,
      IWaitForSortableUniqueId
{
    public string? WaitForSortableUniqueId { get; set; }
    
    public static ResultBox<IEnumerable<WeatherForecastRecord>> HandleFilter(MultiProjectionState<AggregateListProjector<WeatherForecastProjector>> projection, WeatherForecastQuery query, IQueryContext context)
    {
        return projection.Payload.Aggregates.Where(m => m.Value.GetPayload() is WeatherForecast)
            .Select(m => ((WeatherForecast)m.Value.GetPayload(), m.Value.PartitionKeys))
            .Where(tuple => string.IsNullOrEmpty(query.LocationContains) || 
                           tuple.Item1.Location.Contains(query.LocationContains, StringComparison.OrdinalIgnoreCase))
            .Select((tuple) => new WeatherForecastRecord(tuple.PartitionKeys.AggregateId, tuple.Item1.Location,
                tuple.Item1.Date, tuple.Item1.TemperatureC, tuple.Item1.Summary, tuple.Item1.TemperatureC.GetFahrenheit()))
            .ToResultBox();
    }

    public static ResultBox<IEnumerable<WeatherForecastRecord>> HandleSort(IEnumerable<WeatherForecastRecord> filteredList, WeatherForecastQuery query, IQueryContext context)
    {
        return filteredList.OrderBy(m => m.Date).AsEnumerable().ToResultBox();
    }

    [GenerateSerializer]
    public record WeatherForecastRecord(
        Guid WeatherForecastId,
        string Location,
        DateOnly Date,
        TemperatureCelsius TemperatureC,
        string Summary,
        double TemperatureF
    );

}
