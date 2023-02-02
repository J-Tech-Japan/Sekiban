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
    private IAggregatePayloadCommon Payload { get; set; } = CreatePayload();
    public AggregateState<TAggregatePayload> ToState() => ToState<TAggregatePayload>();

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

    public bool GetPayloadTypeIs<TAggregatePayloadExpect>() where TAggregatePayloadExpect : IAggregatePayloadCommon =>
        Payload is TAggregatePayloadExpect;
    public AggregateState<TAggregatePayloadOut> ToState<TAggregatePayloadOut>() where TAggregatePayloadOut : IAggregatePayloadCommon =>
        Payload is TAggregatePayloadOut payloadOut ? new AggregateState<TAggregatePayloadOut>(this, payloadOut)
            : throw new AggregateTypeNotMatchException(typeof(TAggregatePayloadOut), Payload.GetType());

    private static TAggregatePayload CreatePayload()
    {
        if (!typeof(TAggregatePayload).IsParentAggregatePayload())
        {
            return (TAggregatePayload?)Activator.CreateInstance(typeof(TAggregatePayload)) ??
                throw new Exception("Failed to create Aggregate Payload");
        }
        var firstAggregateType = typeof(TAggregatePayload).GetFirstAggregatePayloadTypeFromAggregate();
        var obj = Activator.CreateInstance(firstAggregateType);
        return (TAggregatePayload?)obj ?? throw new Exception("Failed to create Aggregate Payload");
    }

    public override string GetPayloadVersionIdentifier() => Payload.GetPayloadVersionIdentifier();
    protected override Action? GetApplyEventAction(IEvent ev, IEventPayloadCommon eventPayload)
    {
        (ev, eventPayload) = EventHelper.GetConvertedEventAndPayloadIfConverted(ev, eventPayload);
        if (eventPayload is UnregisteredEventPayload || eventPayload is EmptyEventPayload)
        {
            return () => { };
        }
        var eventType = eventPayload.GetEventPayloadType();
        var aggregatePayloadIn = eventPayload.GetAggregatePayloadInType();
        var aggregatePayloadOut = eventPayload.GetAggregatePayloadOutType();
        var methods = GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Where(m => m.Name == nameof(GetApplyEventFunc));
        var method = methods.First(m => m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 3);
        var genericMethod = method?.MakeGenericMethod(aggregatePayloadIn, aggregatePayloadOut, eventType);
        var func = (dynamic?)genericMethod?.Invoke(this, new object[] { eventPayload });
        return () =>
        {
            if (func == null)
            {
                return;
            }
            var result = func((dynamic)Payload, (dynamic)ev);
            Payload = result;
        };
    }

    private Func<TAggregatePayloadIn, Event<TEventPayload>, TAggregatePayloadOut>? GetApplyEventFunc<TAggregatePayloadIn, TAggregatePayloadOut,
        TEventPayload>(
        IEventPayloadCommon eventPayload)
        where TAggregatePayloadIn : IAggregatePayloadCommon
        where TAggregatePayloadOut : IAggregatePayloadCommon
        where TEventPayload : IEventPayload<TAggregatePayloadIn, TAggregatePayloadOut, TEventPayload>
    {
#if NET7_0_OR_GREATER
        return TEventPayload.OnEvent;
#else
        if (eventPayload is IEventPayload<TAggregatePayloadIn, TAggregatePayloadOut, TEventPayload> applicableEvent)
        {
            return applicableEvent.OnEventInstance;
        }
        return null;
#endif
    }

    internal IEvent AddAndApplyEvent<TEventPayload>(TEventPayload eventPayload)
        where TEventPayload : IEventPayloadCommon
    {
        var aggregatePayloadBase = Payload.GetBaseAggregatePayloadType();
        // var aggregatePayloadBase = typeof(TEventPayload);
        var ev = Event<TEventPayload>.GenerateEvent(AggregateId, aggregatePayloadBase, eventPayload);
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
