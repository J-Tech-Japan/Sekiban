using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Aggregate;

public class Aggregate<TAggregatePayload> : AggregateCommonBase,
    ISingleProjectionStateConvertible<AggregateState<TAggregatePayload>>
    where TAggregatePayload : IAggregatePayload, new()
{
    protected TAggregatePayload Payload { get; private set; } = new();
    public AggregateState<TAggregatePayload> ToState() => new(this, Payload);

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

    protected override Action? GetApplyEventAction(IEvent ev, IEventPayload payload)
    {
        var func = GetApplyEventFunc(ev, payload);
        return () =>
        {
            if (func == null) { return; }
            var result = func(Payload, ev);
            Payload = result;
        };
    }
    protected Func<TAggregatePayload, IEvent, TAggregatePayload>? GetApplyEventFunc(IEvent ev, IEventPayload payload)
    {
        if (payload is IApplicableEvent<TAggregatePayload> applicableEvent)
        {
            return applicableEvent.OnEvent;
        }
        return null;
    }

    internal IEvent AddAndApplyEvent<TEventPayload>(TEventPayload eventPayload)
        where TEventPayload : IEventPayload, IApplicableEvent<TAggregatePayload>
    {
        var ev = Event<TEventPayload>.GenerateEvent(AggregateId, typeof(TAggregatePayload), eventPayload);

        if (GetApplyEventAction(ev, eventPayload) is null)
        {
            throw new SekibanEventNotImplementedException($"{eventPayload.GetType().Name} Event not implemented on {GetType().Name} Aggregate");
        }
        // バージョンが変わる前に、イベントには現在のバージョンを入れて動かす
        ev = ev with { Version = Version };
        ApplyEvent(ev);
        ev = ev with { Version = Version };
        return ev;
    }
    protected void CopyPropertiesFromSnapshot(AggregateState<TAggregatePayload> snapshot)
    {
        Payload = snapshot.Payload;
    }
}
