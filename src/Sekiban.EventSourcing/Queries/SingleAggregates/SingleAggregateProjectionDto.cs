namespace Sekiban.EventSourcing.Queries.SingleAggregates;

public record SingleAggregateProjectionDto<TContents>(
    TContents Contents,
    Guid AggregateId,
    bool IsDeleted,
    Guid LastEventId,
    string LastSortableUniqueId,
    int AppliedSnapshotVersion,
    int Version) : ISingleAggregateProjectionContents, ISingleAggregate where TContents : ISingleAggregateProjectionContents;
