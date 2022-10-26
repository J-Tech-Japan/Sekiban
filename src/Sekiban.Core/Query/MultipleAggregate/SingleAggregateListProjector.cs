using Sekiban.Core.Event;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Query.MultipleAggregate;

public class SingleAggregateListProjector<T, Q, P> : IMultipleAggregateProjector<SingleAggregateListProjectionState<Q>>
    where T : ISingleAggregate, ISingleAggregateProjection, ISingleAggregateProjectionStateConvertible<Q>
    where Q : ISingleAggregate
    where P : ISingleAggregateProjector<T>, new()
{
    private T _eventChecker;
    private P _projector = new();
    public SingleAggregateListProjector()
    {
        _eventChecker = _projector.CreateInitialAggregate(Guid.Empty);
        State = new SingleAggregateListProjectionState<Q> { List = List.Select(m => m.ToState()).ToList() };
    }
    private SingleAggregateListProjectionState<Q> State { get; set; }
    public List<T> List
    {
        get;
        private set;
    } = new();
    public void ApplyEvent(IAggregateEvent ev)
    {
        if (_eventChecker.CanApplyEvent(ev))
        {
            if (ev.IsAggregateInitialEvent)
            {
                var aggregate = _projector.CreateInitialAggregate(ev.AggregateId);
                aggregate.ApplyEvent(ev);
                List.Add(aggregate);
            }
            else
            {
                var targetAggregate = List.FirstOrDefault(m => m.AggregateId == ev.AggregateId);
                if (targetAggregate is not null)
                {
                    targetAggregate.ApplyEvent(ev);
                }
            }
        }
        Version++;
        LastEventId = ev.Id;
        LastSortableUniqueId = ev.SortableUniqueId;
    }
    public MultipleAggregateProjectionState<SingleAggregateListProjectionState<Q>> ToState()
    {
        State = new SingleAggregateListProjectionState<Q> { List = List.Select(m => m.ToState()).ToList() };
        return new MultipleAggregateProjectionState<SingleAggregateListProjectionState<Q>>(
            State,
            LastEventId,
            LastSortableUniqueId,
            AppliedSnapshotVersion,
            Version);
    }
    public void ApplySnapshot(MultipleAggregateProjectionState<SingleAggregateListProjectionState<Q>> snapshot)
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
    public IList<string> TargetAggregateNames()
    {
        return new List<string> { _projector.OriginalAggregateType().Name };
    }
    public Guid LastEventId { get; private set; } = Guid.Empty;
    public string LastSortableUniqueId { get; private set; } = string.Empty;
    public int AppliedSnapshotVersion { get; private set; }
    public int Version { get; private set; }
}
