using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public class SingleAggregateListProjector<T, Q, P> : IMultipleAggregateProjector<SingleAggregateProjectionDto<Q>>
    where T : ISingleAggregate, ISingleAggregateProjection, ISingleAggregateProjectionDtoConvertible<Q>
    where Q : ISingleAggregate
    where P : ISingleAggregateProjector<T>, new()
{
    private P _projector = new();
    public List<T> List { get; } = new();
    public void ApplyEvent(AggregateEvent ev)
    {
        if (ev.IsAggregateInitialEvent)
        {
            var aggregate = _projector.CreateInitialAggregate(ev.AggregateId);
            aggregate.ApplyEvent(ev);
            List.Add(aggregate);
        } else
        {
            var targetAggregate = List.FirstOrDefault(m => m.AggregateId == ev.AggregateId);
            if (targetAggregate != null)
            {
                targetAggregate.ApplyEvent(ev);
            }
        }
    }
    public SingleAggregateProjectionDto<Q> ToDto()
    {
        var dto = new SingleAggregateProjectionDto<Q>();

        return dto;
    }
    public void ApplySnapshot(SingleAggregateProjectionDto<Q> snapshot)
    {
        throw new NotImplementedException();
    }
    public Guid LastEventId { get; }
    public string LastSortableUniqueId { get; }
    public int AppliedSnapshotVersion { get; }
    public int Version { get; }
}
