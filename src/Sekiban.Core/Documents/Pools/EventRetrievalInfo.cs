using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Partition;
using Sekiban.Core.Query;
using Sekiban.Core.Query.SingleProjections.Projections;
namespace Sekiban.Core.Documents.Pools;

public interface ISortableIdCondition
{
    public static ISortableIdCondition None => new SortableIdConditionNone();
    public bool OutsideOfRange(SortableUniqueIdValue toCompare);
    public static ISortableIdCondition Since(SortableUniqueIdValue sinceSortableId) =>
        new SinceSortableIdCondition(sinceSortableId);
    public static ISortableIdCondition FromState(IAggregateStateCommon? state) =>
        state?.LastSortableUniqueId is { } lastSortableId ? Since(lastSortableId) : None;
    public static ISortableIdCondition FromMultiProjectionState(IProjection state) =>
        state.LastSortableUniqueId is { } lastSortableId && state.Version > 0 ? Since(lastSortableId) : None;
    public static ISortableIdCondition FromMemoryCacheContainer(IMemoryCacheContainer container) =>
        container.SafeSortableUniqueId is { } lastSortableId ? Since(lastSortableId) : None;
}
public record SortableIdConditionNone : ISortableIdCondition
{
    public bool OutsideOfRange(SortableUniqueIdValue toCompare) => false;
}
public record SinceSortableIdCondition(SortableUniqueIdValue SortableUniqueId) : ISortableIdCondition
{
    public bool OutsideOfRange(SortableUniqueIdValue toCompare) => SortableUniqueId.IsLaterThan(toCompare);
}
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
            ? OptionalValue<string>.Empty
            : OptionalValue.FromNullableValue(rootPartitionKey),
        OptionalValue<IAggregatesStream>.FromValue(aggregatesStream),
        OptionalValue.FromNullableValue(aggregateId),
        sortableIdCondition)
    {
        MaxCount = OptionalValue.FromNullableValue(MaxCount)
    };

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
