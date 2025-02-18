using Sekiban.Pure.Aggregates;
namespace Sekiban.Pure.Orleans.Surrogates;

[RegisterConverter]
public sealed class OrleansAggregateConverter : IConverter<Aggregate, OrleansAggregate>
{
    private readonly OrleansPartitionKeysConverter _partitionKeysConverter = new();

    public Aggregate ConvertFromSurrogate(in OrleansAggregate surrogate) =>
        new(
            surrogate.Payload,
            _partitionKeysConverter.ConvertFromSurrogate(surrogate.PartitionKeys),
            surrogate.Version,
            surrogate.LastSortableUniqueId,
            surrogate.ProjectorVersion,
            surrogate.ProjectorTypeName,
            surrogate.PayloadTypeName);

    public OrleansAggregate ConvertToSurrogate(in Aggregate value) =>
        new(
            value.Payload,
            _partitionKeysConverter.ConvertToSurrogate(value.PartitionKeys),
            value.Version,
            value.LastSortableUniqueId,
            value.ProjectorVersion,
            value.ProjectorTypeName,
            value.PayloadTypeName);
}
[RegisterConverter]
public sealed class
    OrleansAggregateConverter<TAggregatePayload> : IConverter<Aggregate<TAggregatePayload>,
    OrleansAggregate<TAggregatePayload>>
    where TAggregatePayload : IAggregatePayload
{
    private readonly OrleansPartitionKeysConverter _partitionKeysConverter = new();

    public Aggregate<TAggregatePayload> ConvertFromSurrogate(in OrleansAggregate<TAggregatePayload> surrogate) =>
        new(
            surrogate.Payload,
            _partitionKeysConverter.ConvertFromSurrogate(surrogate.PartitionKeys),
            surrogate.Version,
            surrogate.LastSortableUniqueId,
            surrogate.ProjectorVersion,
            surrogate.ProjectorTypeName,
            surrogate.PayloadTypeName);

    public OrleansAggregate<TAggregatePayload> ConvertToSurrogate(in Aggregate<TAggregatePayload> value) =>
        new(
            value.Payload,
            _partitionKeysConverter.ConvertToSurrogate(value.PartitionKeys),
            value.Version,
            value.LastSortableUniqueId,
            value.ProjectorVersion,
            value.ProjectorTypeName,
            value.PayloadTypeName);
}