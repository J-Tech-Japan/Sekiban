using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public class SingleAggregateProjectionDto<Q> : IMultipleAggregateProjectionDto where Q : ISingleAggregate
{
    public List<Q> List { get; }
    public Guid LastEventId { get; }
    public string LastSortableUniqueId { get; }
    public int AppliedSnapshotVersion { get; }
    public int Version { get; }
}
