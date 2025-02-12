using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Exceptions;

namespace Sekiban.Pure.OrleansEventSourcing;

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
    [property:Id(3)]string LastSortableUniqueId,
    [property:Id(4)]string ProjectorVersion,
    [property:Id(5)]string ProjectorTypeName,
    [property:Id(6)]string PayloadTypeName) 
{
    public static IAggregatePayload ConvertPayload(IAggregatePayload payload) => payload switch
    {
        EmptyAggregatePayload => new OrleansEmptyAggregatePayload(),
        OrleansEmptyAggregatePayload => new EmptyAggregatePayload(),
        _ => payload
    };
    public ResultBox<OrleansAggregate<TAggregatePayload>> ToTypedPayload<TAggregatePayload>()
        where TAggregatePayload : IAggregatePayload => Payload is TAggregatePayload typedPayload
        ? ResultBox.FromValue(
            new OrleansAggregate<TAggregatePayload>(typedPayload, PartitionKeys, Version, LastSortableUniqueId))
        : new SekibanAggregateTypeException("Payload is not typed to " + typeof(TAggregatePayload).Name);
}