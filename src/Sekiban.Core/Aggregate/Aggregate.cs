using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Types;
using System.Reflection;
namespace Sekiban.Core.Aggregate;

/// <summary>
///     Common Aggregate Class
///     Contents are defined by implementing <see cref="IAggregatePayload" />.
/// </summary>
/// <typeparam name="TAggregatePayload">User Defined Aggregate Payload</typeparam>
public sealed class Aggregate<TAggregatePayload> : AggregateCommon,
    ISingleProjectionStateConvertible<AggregateState<TAggregatePayload>>
    where TAggregatePayload : IAggregatePayloadCommon
{
    private TAggregatePayload Payload { get; set; } = CreatePayload();

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

    private static TAggregatePayload CreatePayload()
    {
        if (!typeof(TAggregatePayload).IsParentAggregateType())
        {
            return (TAggregatePayload?)Activator.CreateInstance(typeof(TAggregatePayload)) ??
                throw new Exception("Failed to create Aggregate Payload");
        }
        var firstAggregateType = typeof(TAggregatePayload).GetFirstAggregatePayloadTypeFromAggregate();
        var obj = Activator.CreateInstance(firstAggregateType);
        return (TAggregatePayload?)obj ?? throw new Exception("Failed to create Aggregate Payload");
    }

    public override string GetPayloadVersionIdentifier() => Payload.GetPayloadVersionIdentifier();
    protected override Action? GetApplyEventAction(IEvent ev, IEventPayloadCommon payload)
    {
        (ev, payload) = EventHelper.GetConvertedEventAndPayloadIfConverted(ev, payload);
        if (payload is UnregisteredEventPayload || payload is EmptyEventPayload)
        {
            return () => { };
        }
        var eventType = payload.GetEventPayloadType();
        var method = GetType().GetMethod(nameof(GetApplyEventFunc), BindingFlags.Instance | BindingFlags.NonPublic);
        var genericMethod = method?.MakeGenericMethod(eventType);
        var func = (dynamic?)genericMethod?.Invoke(this, new object[] { payload });
        return () =>
        {
            if (func == null)
            {
                return;
            }
            var result = func(Payload, (dynamic)ev);
            Payload = result;
        };
    }

    private Func<TAggregatePayload, Event<TEventPayload>, TAggregatePayload>? GetApplyEventFunc<TEventPayload>(
        IEventPayloadCommon payload)
        where TEventPayload : IEventPayload<TAggregatePayload, TEventPayload>
    {
#if NET7_0_OR_GREATER
        return TEventPayload.OnEvent;
#else
        if (payload is IEventPayload<TAggregatePayload, TEventPayload> applicableEvent)
        {
            return applicableEvent.OnEventInstance;
        }
        return null;
#endif
    }
    private Func<TAggregatePayload, Event<TEventPayload>, TAggregatePayload>? GetApplyEventFunc<TAggregatePayloadIn, TAggregatePayloadOut,
        TEventPayload>(
        IEventPayloadCommon payload)
        where TEventPayload : IEventPayload<TAggregatePayload, TEventPayload>
    {
#if NET7_0_OR_GREATER
        return TEventPayload.OnEvent;
#else
        if (payload is IEventPayload<TAggregatePayload, TEventPayload> applicableEvent)
        {
            return applicableEvent.OnEventInstance;
        }
        return null;
#endif
    }

    internal IEvent AddAndApplyEvent<TEventPayload>(TEventPayload eventPayload)
        where TEventPayload : IEventPayloadCommon, IEventPayload<TAggregatePayload, TEventPayload>
    {
        var ev = Event<TEventPayload>.GenerateEvent(AggregateId, typeof(TAggregatePayload), eventPayload);
        var result = GetApplyEventAction(ev, eventPayload);
        if (result is null)
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
