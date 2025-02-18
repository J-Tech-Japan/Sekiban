using ResultBoxes;
using Sekiban.Pure.Documents;
namespace Sekiban.Pure.Events;

public record EventRetrievalInfo(
    OptionalValue<string> RootPartitionKey,
    OptionalValue<IAggregatesStream> AggregateStream,
    OptionalValue<Guid> AggregateId,
    ISortableIdCondition SortableIdCondition)
{
    public OptionalValue<int> MaxCount { get; init; } = OptionalValue<int>.Empty;

    public static EventRetrievalInfo FromNullableValues(
        string? rootPartitionKey,
        IAggregatesStream aggregatesStream,
        Guid? aggregateId,
        ISortableIdCondition sortableIdCondition,
        int? MaxCount = null) => new(
        string.IsNullOrWhiteSpace(rootPartitionKey)
            ? (aggregateId.HasValue ? OptionalValue.FromValue(IDocument.DefaultRootPartitionKey) : OptionalValue<string>.Empty) 
            : OptionalValue.FromNullableValue(rootPartitionKey),
        OptionalValue<IAggregatesStream>.FromValue(aggregatesStream),
        OptionalValue.FromNullableValue(aggregateId),
        sortableIdCondition)
    {
        MaxCount = OptionalValue.FromNullableValue(MaxCount)
    };
    public static EventRetrievalInfo All => new(
        OptionalValue<string>.Empty,
        OptionalValue<IAggregatesStream>.Empty,
        OptionalValue<Guid>.Empty,
        SortableIdConditionNone.None);

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
                aggregateName => PartitionKeys.Existing(AggregateId.GetValue(),aggregateName, RootPartitionKey.GetValue()).ToPrimaryKeysString());
    public static EventRetrievalInfo FromPartitionKeys(PartitionKeys partitionKeys) =>
        new(
            OptionalValue.FromValue(partitionKeys.RootPartitionKey),
            OptionalValue<IAggregatesStream>.FromValue(new AggregateGroupStream(partitionKeys.Group)),
            OptionalValue.FromValue(partitionKeys.AggregateId),
            SortableIdConditionNone.None);
}