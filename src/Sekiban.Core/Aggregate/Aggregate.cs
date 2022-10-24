using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Aggregate;

public class Aggregate<TPayload> : AggregateCommonBase, ISingleAggregateProjectionDtoConvertible<AggregateState<TPayload>>
    where TPayload : IAggregatePayload, new()
{
    protected TPayload Payload { get; private set; } = new();
    private bool IsDeleted => Payload is IDeletableAggregatePayload { IsDeleted: true };
    public AggregateState<TPayload> ToState()
    {
        return new AggregateState<TPayload>(this, Payload);
    }

    public void ApplySnapshot(AggregateState<TPayload> snapshot)
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

    public TAggregate Clone<TAggregate>() where TAggregate : Aggregate<TPayload>, new()
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
    protected Func<TPayload, IAggregateEvent, TPayload>? GetApplyEventFunc(IAggregateEvent ev, IEventPayload payload)
    {
        if (payload is IApplicableEvent<TPayload> applicableEvent)
        {
            return applicableEvent.OnEvent;
        }
        return null;
    }

    internal static IAggregateEvent AddAndApplyEvent<TEventPayload>(Aggregate<TPayload> aggregate, TEventPayload eventPayload)
        where TEventPayload : IEventPayload, IApplicableEvent<TPayload>
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
    protected void CopyPropertiesFromSnapshot(AggregateState<TPayload> snapshot)
    {
        Payload = snapshot.Payload;
    }
}
