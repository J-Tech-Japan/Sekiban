using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.MultiProjections;

/// <summary>
///     General Single Projection List Projector
/// </summary>
/// <typeparam name="TProjection"></typeparam>
/// <typeparam name="TState"></typeparam>
/// <typeparam name="TProjector"></typeparam>
public class SingleProjectionListProjector<TProjection, TState, TProjector> : IMultiProjector<SingleProjectionListState<TState>>
    where TProjection : IAggregateCommon, ISingleProjection, ISingleProjectionStateConvertible<TState>
    where TState : IAggregateStateCommon
    where TProjector : ISingleProjector<TProjection>, new()
{
    private readonly TProjector _projector = new();

    private SingleProjectionListState<TState> State { get; set; }

    public List<TProjection> List { get; private set; } = new();

    public SingleProjectionListProjector()
    {
        State = new SingleProjectionListState<TState> { List = List.Select(m => m.ToState()).ToList() };
    }
    public bool EventShouldBeApplied(IEvent ev) => ev.GetSortableUniqueId().IsLaterThanOrEqual(new SortableUniqueIdValue(LastSortableUniqueId));

    public void ApplyEvent(IEvent ev)
    {
        var targetAggregate = List.FirstOrDefault(m => m.AggregateId == ev.AggregateId);
        if (targetAggregate is null)
        {
            var aggregate = _projector.CreateInitialAggregate(ev.AggregateId);
            aggregate.ApplyEvent(ev);
            List.Add(aggregate);
        } else
        {
            targetAggregate.ApplyEvent(ev);
        }

        Version++;
        LastEventId = ev.Id;
        LastSortableUniqueId = ev.SortableUniqueId;
    }

    public MultiProjectionState<SingleProjectionListState<TState>> ToState()
    {
        State = new SingleProjectionListState<TState> { List = List.Select(m => m.ToState()).ToList() };
        return new MultiProjectionState<SingleProjectionListState<TState>>(
            State,
            LastEventId,
            LastSortableUniqueId,
            AppliedSnapshotVersion,
            Version,
            RootPartitionKey);
    }

    public void ApplySnapshot(MultiProjectionState<SingleProjectionListState<TState>> snapshot)
    {
        Version = snapshot.Version;
        LastEventId = snapshot.LastEventId;
        LastSortableUniqueId = snapshot.LastSortableUniqueId;
        AppliedSnapshotVersion = snapshot.Version;
        State = snapshot.Payload;
        List = State.List.Select(
                m =>
                {
                    var aggregate = _projector.CreateInitialAggregate(m.AggregateId);
                    aggregate.ApplySnapshot(m);
                    return aggregate;
                })
            .ToList();
    }

    public IList<string> TargetAggregateNames() => new List<string> { _projector.GetOriginalAggregatePayloadType().Name };

    public Guid LastEventId { get; private set; } = Guid.Empty;
    public string LastSortableUniqueId { get; private set; } = string.Empty;
    public int AppliedSnapshotVersion { get; private set; }
    public int Version { get; private set; }
    public string RootPartitionKey { get; } = string.Empty;
    public string GetPayloadVersionIdentifier() => _projector.GetPayloadVersionIdentifier();
}
