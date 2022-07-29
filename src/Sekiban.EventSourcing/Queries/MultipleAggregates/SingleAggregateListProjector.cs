using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public class SingleAggregateListProjector<T, Q, P> : IMultipleAggregateProjector<SingleAggregateProjectionDto<Q>>
    where T : ISingleAggregate, ISingleAggregateProjection, ISingleAggregateProjectionDtoConvertible<Q>
    where Q : ISingleAggregate
    where P : ISingleAggregateProjector<T>, new()
{
    private T _eventChecker;
    private P _projector = new();
    public List<T> List { get; } = new();
    public SingleAggregateListProjector() =>
        _eventChecker = _projector.CreateInitialAggregate(Guid.Empty);
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
    public SingleAggregateProjectionDto<Q> ToDto()
    {
        var dto = new SingleAggregateProjectionDto<Q>(
            List.Select(m => m.ToDto()).ToList(),
            LastEventId,
            LastSortableUniqueId,
            AppliedSnapshotVersion,
            Version);
        return dto;
    }
    public void ApplySnapshot(SingleAggregateProjectionDto<Q> snapshot)
    {
        throw new NotImplementedException();
    }
    public IList<string> TargetAggregateNames() =>
        new List<string> { _projector.OriginalAggregateType().Name };
    public Guid LastEventId { get; private set; } = Guid.Empty;
    public string LastSortableUniqueId { get; private set; } = string.Empty;
    public int AppliedSnapshotVersion { get; } = 0;
    public int Version { get; private set; }
}
