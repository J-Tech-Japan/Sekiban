using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Aggregate;

public class AggregateIdentifier<TAggregatePayload> : AggregateIdentifierCommonBase,
    ISingleProjectionStateConvertible<AggregateIdentifierState<TAggregatePayload>>
    where TAggregatePayload : IAggregatePayload, new()
{
    protected TAggregatePayload Payload { get; private set; } = new();
    public AggregateIdentifierState<TAggregatePayload> ToState() => new AggregateIdentifierState<TAggregatePayload>(this, Payload);

    public void ApplySnapshot(AggregateIdentifierState<TAggregatePayload> snapshot)
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

    public TAggregate Clone<TAggregate>() where TAggregate : AggregateIdentifier<TAggregatePayload>, new()
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
        var ev = eventPayload is ICreatedEventPayload
            ? Event<TEventPayload>.CreatedEvent(AggregateId, typeof(TAggregatePayload), eventPayload)
            : Event<TEventPayload>.ChangedEvent(AggregateId, typeof(TAggregatePayload), eventPayload);

        if (GetApplyEventAction(ev, eventPayload) is null)
        {
            throw new SekibanEventNotImplementedException();
        }
        // バージョンが変わる前に、イベントには現在のバージョンを入れて動かす
        ev = ev with { Version = Version };
        ApplyEvent(ev);
        ev = ev with { Version = Version };
        return ev;
    }
    protected void CopyPropertiesFromSnapshot(AggregateIdentifierState<TAggregatePayload> snapshot)
    {
        Payload = snapshot.Payload;
    }
}
