using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Orleans.Surrogates;

[GenerateSerializer]
public record struct OrleansAggregateListProjector<TAggregateProjector>(
    [property: Id(0)] Dictionary<OrleansPartitionKeys, OrleansAggregate> Aggregates)
    where TAggregateProjector : IAggregateProjector, new();