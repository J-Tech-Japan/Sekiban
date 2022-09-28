namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public record MultipleAggregateProjectionContentsDto<TContents>(
    TContents Contents,
    Guid LastEventId,
    string LastSortableUniqueId,
    int AppliedSnapshotVersion,
    int Version) : IProjection where TContents : IMultipleAggregateProjectionContents
{
}
