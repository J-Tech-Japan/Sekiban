using Orleans;
using ResultBoxes;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;

namespace DaprSekiban.Domain.Projections.Count;

[GenerateSerializer]
public record WeatherCountQuery(string Location) : IMultiProjectionQuery<WeatherCountMultiProjection,WeatherCountQuery,int>
{
    public static ResultBox<int> HandleQuery(MultiProjectionState<WeatherCountMultiProjection> projection, WeatherCountQuery query, IQueryContext context)
        => projection.Payload.WeatherCounts.ContainsKey(query.Location) ? projection.Payload.WeatherCounts[query.Location] : 0;
}