using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.Queries;
using Sekiban.EventSourcing.Snapshots;
namespace Sekiban.EventSourcing.Aggregates;

public interface IAggregate : ISingleAggregate, ISingleAggregateProjection
{
    ReadOnlyCollection<AggregateEvent> Events { get; }
    ReadOnlyCollection<SnapshotDocument> Snapshots { get; }
}
