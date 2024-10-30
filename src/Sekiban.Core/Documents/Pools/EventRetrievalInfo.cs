using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Partition;
namespace Sekiban.Core.Documents.Pools;

public record EventRetrievalInfo(
    OptionalValue<string> RootPartitionKey,
    OptionalValue<IAggregatesStream> AggregateStream,
    OptionalValue<Guid> AggregateId,
    OptionalValue<SortableUniqueIdValue> SinceSortableUniqueId)
{
    public static EventRetrievalInfo FromNullableValues(
        string? rootPartitionKey,
        IAggregatesStream aggregatesStream,
        Guid? aggregateId,
        string? sinceSortableUniqueId) => new(
        string.IsNullOrWhiteSpace(rootPartitionKey)
            ? OptionalValue<string>.Empty
            : OptionalValue.FromNullableValue(rootPartitionKey),
        OptionalValue<IAggregatesStream>.FromValue(aggregatesStream),
        OptionalValue.FromNullableValue(aggregateId),
        OptionalValue.FromNullableValue(sinceSortableUniqueId).Remap(id => new SortableUniqueIdValue(id)));

    public bool GetIsPartition() => AggregateId.HasValue;
    public bool HasAggregateStream() =>
        AggregateStream.HasValue && AggregateStream.GetValue().GetStreamNames().Count > 0;
    public bool HasRootPartitionKey() => RootPartitionKey.HasValue;

    public ResultBox<string> GetPartitionKey() =>
        ResultBox
            .UnitValue
            .Verify(
                () => GetIsPartition() ? ExceptionOrNone.None : new ApplicationException("Partition Key is not set"))
            .Verify(
                () => HasAggregateStream()
                    ? ExceptionOrNone.None
                    : new ApplicationException("Aggregate Stream is not set"))
            .Conveyor(() => AggregateStream.GetValue().GetSingleStreamName())
            .Verify(
                () => HasRootPartitionKey()
                    ? ExceptionOrNone.None
                    : new ApplicationException("Root Partition Key is not set"))
            .Remap(
                aggregateName => PartitionKeyGenerator.ForEventGroup(
                    AggregateId.GetValue(),
                    aggregateName,
                    RootPartitionKey.GetValue()));

    public AggregateContainerGroup GetAggregateContainerGroup() =>
        AggregateStream.HasValue
            ? AggregateStream.GetValue().GetAggregateContainerGroup()
            : AggregateContainerGroup.Default;
}
