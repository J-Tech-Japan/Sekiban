using Sekiban.Pure.Aggregates;
namespace Sekiban.Pure.Orleans.Surrogates;

[GenerateSerializer]
public record struct OrleansAggregate(
    [property: Id(0)] IAggregatePayload Payload,
    [property: Id(1)] OrleansPartitionKeys PartitionKeys,
    [property: Id(2)] int Version,
    [property: Id(3)] string LastSortableUniqueId,
    [property: Id(4)] string ProjectorVersion,
    [property: Id(5)] string ProjectorTypeName,
    [property: Id(6)] string PayloadTypeName);
[GenerateSerializer]
public record struct OrleansAggregate<TAggregatePayload>(
    [property: Id(0)] TAggregatePayload Payload,
    [property: Id(1)] OrleansPartitionKeys PartitionKeys,
    [property: Id(2)] int Version,
    [property: Id(3)] string LastSortableUniqueId,
    [property: Id(4)] string ProjectorVersion,
    [property: Id(5)] string ProjectorTypeName,
    [property: Id(6)] string PayloadTypeName) where TAggregatePayload : IAggregatePayload;
