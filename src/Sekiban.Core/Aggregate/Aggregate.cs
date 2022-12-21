using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Aggregate;

/// <summary>
///     Common Aggregate Class
///     Contents are defined by implementing <see cref="IAggregatePayload" />.
/// </summary>
/// <typeparam name="TAggregatePayload">User Defined Aggregate Payload</typeparam>
public sealed class Aggregate<TAggregatePayload> : AggregateCommon,
    ISingleProjectionStateConvertible<AggregateState<TAggregatePayload>>
    where TAggregatePayload : IAggregatePayload, new()
{
    private TAggregatePayload Payload { get; set; } = new();

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

    public override string GetPayloadVersionIdentifier() => Payload.GetPayloadVersionIdentifier();
    protected override Action? GetApplyEventAction(IEvent ev, IEventPayloadCommon payload)
    {
        (ev, payload) = EventHelper.GetConvertedEventAndPayloadIfConverted(ev, payload);
        var func = GetApplyEventFunc(ev, payload);
        return () =>
        {
            if (func == null)
            {
                return;
            }
            var result = func(Payload, ev);
            Payload = result;
        };
    }

    private Func<TAggregatePayload, IEvent, TAggregatePayload>? GetApplyEventFunc(
        IEvent ev,
        IEventPayloadCommon payload)
    {
        if (payload is IEventPayload<TAggregatePayload> applicableEvent)
        {
            return applicableEvent.OnEvent;
        }
        return null;
    }

    internal IEvent AddAndApplyEvent<TEventPayload>(TEventPayload eventPayload)
        where TEventPayload : IEventPayloadCommon, IEventPayload<TAggregatePayload>
    {
        var ev = Event<TEventPayload>.GenerateEvent(AggregateId, typeof(TAggregatePayload), eventPayload);
        if (GetApplyEventAction(ev, eventPayload) is null)
        {
            throw new SekibanEventNotImplementedException(
                $"{eventPayload.GetType().Name} Event not implemented on {GetType().Name} Aggregate");
        }
        ev = ev with { Version = Version };
        ApplyEvent(ev);
        ev = ev with { Version = Version };
        return ev;
    }

    private void CopyPropertiesFromSnapshot(AggregateState<TAggregatePayload> snapshot)
    {
        Payload = snapshot.Payload;
    }
}
