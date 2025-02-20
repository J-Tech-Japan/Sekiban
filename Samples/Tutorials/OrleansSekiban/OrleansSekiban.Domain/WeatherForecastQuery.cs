using ResultBoxes;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;

namespace OrleansSekiban.Domain;

[GenerateSerializer]
public record WeatherForecastQuery(string LocationContains)
    : IMultiProjectionListQuery<AggregateListProjector<WeatherForecastProjector>, WeatherForecastQuery, WeatherForecast>
{
    public static ResultBox<IEnumerable<WeatherForecast>> HandleFilter(
        MultiProjectionState<AggregateListProjector<WeatherForecastProjector>> projection, WeatherForecastQuery query,
        IQueryContext context)
    {
        return projection.Payload.Aggregates.Select(m => m.Value.Payload)
            .Where(x => x.GetType().IsAssignableTo(typeof(WeatherForecast))).Cast<WeatherForecast>()
            .Where(x => string.IsNullOrEmpty(query.LocationContains) || x.Location.Contains(query.LocationContains)).ToResultBox();
    }

    public static ResultBox<IEnumerable<WeatherForecast>> HandleSort(IEnumerable<WeatherForecast> filteredList,
        WeatherForecastQuery query, IQueryContext context)
    {
        return filteredList.OrderBy(m => m.Location).AsEnumerable().ToResultBox();
    }
}