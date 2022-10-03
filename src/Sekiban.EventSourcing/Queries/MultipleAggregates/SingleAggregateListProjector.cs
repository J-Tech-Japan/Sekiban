using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public class SingleAggregateListProjector<T, Q, P> : IMultipleAggregateProjector<SingleAggregateListProjectionDto<Q>>
    where T : ISingleAggregate, ISingleAggregateProjection, ISingleAggregateProjectionDtoConvertible<Q>
    where Q : ISingleAggregate
    where P : ISingleAggregateProjector<T>, new()
{
    private T _eventChecker;
    private P _projector = new();
    private SingleAggregateListProjectionDto<Q> Contents { get; set; }
    public List<T> List
    {
        get;
        private set;
    } = new();
    public SingleAggregateListProjector()
    {
        _eventChecker = _projector.CreateInitialAggregate(Guid.Empty);
        Contents = new SingleAggregateListProjectionDto<Q> { List = List.Select(m => m.ToDto()).ToList() };
    }
    public void ApplyEvent(IAggregateEvent ev)
    {
        if (_eventChecker.CanApplyEvent(ev))
        {
            if (ev.IsAggregateInitialEvent)
            {
                var aggregate = _projector.CreateInitialAggregate(ev.AggregateId);
                aggregate.ApplyEvent(ev);
                List.Add(aggregate);
            } else
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
    public MultipleAggregateProjectionContentsDto<SingleAggregateListProjectionDto<Q>> ToDto()
    {
        Contents = new SingleAggregateListProjectionDto<Q> { List = List.Select(m => m.ToDto()).ToList() };
        return new MultipleAggregateProjectionContentsDto<SingleAggregateListProjectionDto<Q>>(
            Contents,
            LastEventId,
            LastSortableUniqueId,
            AppliedSnapshotVersion,
            Version);
    }
    public void ApplySnapshot(MultipleAggregateProjectionContentsDto<SingleAggregateListProjectionDto<Q>> snapshot)
    {
        Version = snapshot.Version;
        LastEventId = snapshot.LastEventId;
        LastSortableUniqueId = snapshot.LastSortableUniqueId;
        AppliedSnapshotVersion = snapshot.Version;
        Contents = snapshot.Contents;
        List = Contents.List.Select(
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
