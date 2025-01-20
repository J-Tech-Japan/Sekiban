using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Exception;
using Sekiban.Pure.OrleansEventSourcing;

namespace AspireEventSample.ApiService.Grains;

[GenerateSerializer]
public record OrleansAggregate<TAggregatePayload>(
    TAggregatePayload Payload,
    OrleansPartitionKeys PartitionKeys,
    int Version,
    string LastSortableUniqueId) where TAggregatePayload : IAggregatePayload
{
    public IAggregatePayload GetPayload()
    {
        return Payload;
    }
}

[GenerateSerializer]
public record OrleansAggregate(
    [property:Id(0)]IAggregatePayload Payload,
    [property:Id(1)]OrleansPartitionKeys PartitionKeys,
    [property:Id(2)]int Version,
    [property:Id(3)]string LastSortableUniqueId) 
{
    public static OrleansAggregate FromAggregate(IAggregate aggregate)
        => new OrleansAggregate(aggregate.GetPayload(), aggregate.PartitionKeys.ToOrleansPartitionKeys(), aggregate.Version,
            aggregate.LastSortableUniqueId);
    
    public ResultBox<OrleansAggregate<TAggregatePayload>> ToTypedPayload<TAggregatePayload>()
        where TAggregatePayload : IAggregatePayload => Payload is TAggregatePayload typedPayload
        ? ResultBox.FromValue(
            new OrleansAggregate<TAggregatePayload>(typedPayload, PartitionKeys, Version, LastSortableUniqueId))
        : new SekibanAggregateTypeException("Payload is not typed to " + typeof(TAggregatePayload).Name);
}

public static class OrleansAggregateExtensions
{
    public static OrleansAggregate ToOrleansAggregate(this IAggregate aggregate)
    {
        return new OrleansAggregate(aggregate.GetPayload(), aggregate.PartitionKeys.ToOrleansPartitionKeys(), aggregate.Version,
            aggregate.LastSortableUniqueId);
    }
}
