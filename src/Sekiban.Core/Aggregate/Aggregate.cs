using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Aggregate;

public class Aggregate<TAggregatePayload> : AggregateCommonBase, ISingleAggregateProjectionDtoConvertible<AggregateState<TAggregatePayload>>
    where TAggregatePayload : IAggregatePayload, new()
{
    protected TAggregatePayload Payload { get; private set; } = new();
    private bool IsDeleted => Payload is IDeletableAggregatePayload { IsDeleted: true };
    public AggregateState<TAggregatePayload> ToState()
    {
        return new AggregateState<TAggregatePayload>(this, Payload);
    }

    public void ApplySnapshot(AggregateState<TAggregatePayload> snapshot)
    {
        _basicInfo = _basicInfo with
        {
            Version = snapshot.Version,
            LastEventId = snapshot.LastEventId,
            LastSortableUniqueId = snapshot.LastSortableUniqueId,
            AppliedSnapshotVersion = snapshot.Version
        };
        CopyPropertiesFromSnapshot(snapshot);
    }

    public TAggregate Clone<TAggregate>() where TAggregate : Aggregate<TAggregatePayload>, new()
    {
        var clone = new TAggregate { _basicInfo = _basicInfo, Payload = Payload };
        return clone;
    }

    protected override Action? GetApplyEventAction(IAggregateEvent ev, IEventPayload payload)
    {
        var func = GetApplyEventFunc(ev, payload);
        return () =>
        {
            if (func == null) { return; }
            var result = func(Payload, ev);
            Payload = result;
        };
    }
    protected Func<TAggregatePayload, IAggregateEvent, TAggregatePayload>? GetApplyEventFunc(IAggregateEvent ev, IEventPayload payload)
    {
        if (payload is IApplicableEvent<TAggregatePayload> applicableEvent)
        {
            return applicableEvent.OnEvent;
        }
        return null;
    }

    internal static IAggregateEvent AddAndApplyEvent<TEventPayload>(Aggregate<TAggregatePayload> aggregate, TEventPayload eventPayload)
        where TEventPayload : IEventPayload, IApplicableEvent<TAggregatePayload>
    {
        var ev = eventPayload is ICreatedEventPayload
            ? AggregateEvent<TEventPayload>.CreatedEvent(aggregate.AggregateId, aggregate.GetType(), eventPayload)
            : AggregateEvent<TEventPayload>.ChangedEvent(aggregate.AggregateId, aggregate.GetType(), eventPayload);

        if (aggregate.GetApplyEventAction(ev, eventPayload) is null)
        {
            throw new SekibanEventNotImplementedException();
        }
        // バージョンが変わる前に、イベントには現在のバージョンを入れて動かす
        ev = ev with { Version = aggregate.Version };
        aggregate.ApplyEvent(ev);
        ev = ev with { Version = aggregate.Version };
        return ev;
    }
    protected void CopyPropertiesFromSnapshot(AggregateState<TAggregatePayload> snapshot)
    {
        Payload = snapshot.Payload;
    }
}
