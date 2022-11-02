using Sekiban.Core.Event;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.MultProjections;

public class SingleProjectionListProjector<TProjection, TState, TProjector> : IMultiProjector<SingleProjectionListState<TState>>
    where TProjection : IAggregateCommon, ISingleProjection, ISingleProjectionStateConvertible<TState>
    where TState : IAggregateCommon
    where TProjector : ISingleProjector<TProjection>, new()
{
    private TProjection _eventChecker;
    private TProjector _projector = new();
    public SingleProjectionListProjector()
    {
        _eventChecker = _projector.CreateInitialAggregate(Guid.Empty);
        State = new SingleProjectionListState<TState> { List = List.Select(m => m.ToState()).ToList() };
    }
    private SingleProjectionListState<TState> State { get; set; }
    public List<TProjection> List
    {
        get;
        private set;
    } = new();
    public void ApplyEvent(IEvent ev)
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
    public MultiProjectionState<SingleProjectionListState<TState>> ToState()
    {
        State = new SingleProjectionListState<TState> { List = List.Select(m => m.ToState()).ToList() };
        return new MultiProjectionState<SingleProjectionListState<TState>>(
            State,
            LastEventId,
            LastSortableUniqueId,
            AppliedSnapshotVersion,
            Version);
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
    public IList<string> TargetAggregateNames() => new List<string> { _projector.OriginalAggregateType().Name };
    public Guid LastEventId { get; private set; } = Guid.Empty;
    public string LastSortableUniqueId { get; private set; } = string.Empty;
    public int AppliedSnapshotVersion { get; private set; }
    public int Version { get; private set; }
}
