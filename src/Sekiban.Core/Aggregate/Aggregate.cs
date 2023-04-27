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
    public bool GetPayloadTypeIs(Type expect)
    {
        if (!expect.IsAggregatePayloadType()) { return false; }
        var method = GetType().GetMethods().FirstOrDefault(m => m.Name == nameof(GetPayloadTypeIs) && m.IsGenericMethod);
        var genericMethod = method?.MakeGenericMethod(expect);
        return (bool?)genericMethod?.Invoke(this, null) ?? false;
    }
    public AggregateState<TAggregatePayload> ToState() => ToState<TAggregatePayload>();
    public bool CanApplySnapshot(IAggregateStateCommon snapshot) =>
        snapshot is not null && typeof(TAggregatePayload).IsAssignableTo(snapshot.GetType());
    public void ApplySnapshot(IAggregateStateCommon snapshot)
    {
        ApplyBaseInfo(snapshot);
        Payload = snapshot.GetPayload();
    }

    public bool GetPayloadTypeIs<TAggregatePayloadExpect>() =>
        Payload is TAggregatePayloadExpect;

    private void ApplyBaseInfo(IAggregateCommon snapshot)
    {
        _basicInfo = _basicInfo with
        {
            Version = snapshot.Version,
            LastEventId = snapshot.LastEventId,
            LastSortableUniqueId = snapshot.LastSortableUniqueId,
            AppliedSnapshotVersion = snapshot.Version
        };
    }

    public override void ApplyEvent(IEvent ev)
    {
        var result = GetAggregatePayloadWithAppliedEvent(Payload, ev);
        if (result is null)
        {
            return;
        }
        if (ev.Id == LastEventId)
        {
            return;
        }
        Payload = result;
        _basicInfo = _basicInfo with
        {
            LastEventId = ev.Id, LastSortableUniqueId = ev.SortableUniqueId, Version = Version + 1
        };
    }
    public AggregateState<TAggregatePayloadOut> ToState<TAggregatePayloadOut>() where TAggregatePayloadOut : IAggregatePayloadCommon =>
        Payload is TAggregatePayloadOut payloadOut ? new AggregateState<TAggregatePayloadOut>(this, payloadOut)
            : throw new AggregateTypeNotMatchException(typeof(TAggregatePayloadOut), Payload.GetType());

    private static IAggregatePayloadCommon CreatePayload()
    {
        var baseType = typeof(TAggregatePayload).GetBaseAggregatePayloadTypeFromAggregate();
        if (!baseType.IsParentAggregatePayload())
        {
            return (IAggregatePayloadCommon?)Activator.CreateInstance(
                    baseType) ??
                throw new Exception("Failed to create Aggregate Payload");
        }
        var firstAggregateType = typeof(TAggregatePayload).GetFirstAggregatePayloadTypeFromAggregate();
        var obj = Activator.CreateInstance(firstAggregateType);
        return (IAggregatePayloadCommon?)obj ?? throw new Exception("Failed to create Aggregate Payload");
    }

    public override string GetPayloadVersionIdentifier() => Payload.GetPayloadVersionIdentifier();
    protected override IAggregatePayloadCommon? GetAggregatePayloadWithAppliedEvent(object aggregatePayload, IEvent ev)
    {
        (ev, var eventPayload) = EventHelper.GetConvertedEventAndPayloadIfConverted(ev, ev.GetPayload());
        if (eventPayload is UnregisteredEventPayload || eventPayload is EmptyEventPayload)
        {
            return null;
        }
        var eventType = eventPayload.GetEventPayloadType();
        var aggregatePayloadIn = eventPayload.GetAggregatePayloadInType();
        if (!aggregatePayloadIn.IsInstanceOfType(Payload)) { return null; }

        var aggregatePayloadOut = eventPayload.GetAggregatePayloadOutType();
        var methods = GetType().GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Where(m => m.Name == nameof(ApplyEventToAggregatePayload));
        var method = methods.First(m => m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 3);
        var genericMethod = method?.MakeGenericMethod(aggregatePayloadIn, aggregatePayloadOut, eventType);
        return (IAggregatePayloadCommon?)genericMethod?.Invoke(this, new object[] { Payload, ev });
    }

    private static TAggregatePayloadOut? ApplyEventToAggregatePayload<TAggregatePayloadIn, TAggregatePayloadOut,
        TEventPayload>(TAggregatePayloadIn aggregatePayload, Event<TEventPayload> ev)
        where TAggregatePayloadIn : IAggregatePayloadCommon
        where TAggregatePayloadOut : IAggregatePayloadCommon
        where TEventPayload : IEventPayload<TAggregatePayloadIn, TAggregatePayloadOut, TEventPayload>
    {
#if NET7_0_OR_GREATER
        return TEventPayload.OnEvent(aggregatePayload, ev);
#else
        if (ev.Payload is IEventPayload<TAggregatePayloadIn, TAggregatePayloadOut, TEventPayload> applicableEvent)
        {
            return applicableEvent.OnEventInstance(aggregatePayload, ev);
        }
        return default;
#endif
    }

    internal IEvent AddAndApplyEvent<TEventPayload>(TEventPayload eventPayload)
        where TEventPayload : IEventPayloadCommon
    {
        var aggregatePayloadBase = Payload.GetBaseAggregatePayloadType();
        // var aggregatePayloadBase = typeof(TEventPayload);
        var ev = Event<TEventPayload>.GenerateEvent(AggregateId, aggregatePayloadBase, eventPayload);
        var result = GetAggregatePayloadWithAppliedEvent(Payload, ev);
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
