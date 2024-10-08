using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Types;
using System.Reflection;
namespace Sekiban.Core.Aggregate;

/// <summary>
///     Common Aggregate Class
///     Contents are defined by implementing <see cref="IAggregatePayload{TAggrfegatePayload}" />.
/// </summary>
/// <typeparam name="TAggregatePayload">User Defined Aggregate Payload</typeparam>
public sealed class Aggregate<TAggregatePayload> : AggregateCommon,
    ISingleProjectionStateConvertible<AggregateState<TAggregatePayload>>
    where TAggregatePayload : IAggregatePayloadCommon
{
    public bool IsNew => Version == 0;
    private IAggregatePayloadCommon Payload { get; set; } = CreatePayloadCommon<TAggregatePayload>();
    public bool GetPayloadTypeIs(Type expect)
    {
        if (!expect.IsAggregatePayloadType()) { return false; }
        var method = GetType()
            .GetMethods()
            .FirstOrDefault(m => m.Name == nameof(GetPayloadTypeIs) && m.IsGenericMethod);
        var genericMethod = method?.MakeGenericMethod(expect);
        return (bool?)genericMethod?.Invoke(this, null) ?? false;
    }
    public AggregateState<TAggregatePayload> ToState() => ToState<TAggregatePayload>();
    public bool CanApplySnapshot(IAggregateStateCommon? snapshot) => snapshot?.GetPayload() is TAggregatePayload;
    public void ApplySnapshot(IAggregateStateCommon snapshot)
    {
        ApplyBaseInfo(snapshot);
        Payload = snapshot.GetPayload();
    }

    public bool GetPayloadTypeIs<TAggregatePayloadExpect>() => Payload is TAggregatePayloadExpect;

    private void ApplyBaseInfo(IAggregateCommon snapshot)
    {
        _basicInfo = _basicInfo with
        {
            AggregateId = snapshot.AggregateId,
            Version = snapshot.Version,
            LastEventId = snapshot.LastEventId,
            LastSortableUniqueId = snapshot.LastSortableUniqueId,
            AppliedSnapshotVersion = snapshot.Version,
            RootPartitionKey = snapshot.RootPartitionKey
        };
    }

    public override void ApplyEvent(IEvent ev)
    {
        if (!string.IsNullOrEmpty(LastSortableUniqueId) &&
            new SortableUniqueIdValue(LastSortableUniqueId).IsLaterThanOrEqual(
                new SortableUniqueIdValue(ev.SortableUniqueId)))
        {
            return;
        }
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
            LastEventId = ev.Id, LastSortableUniqueId = ev.SortableUniqueId, Version = Version + 1,
            RootPartitionKey = ev.RootPartitionKey
        };
    }
    public AggregateState<TAggregatePayloadOut> ToState<TAggregatePayloadOut>()
        where TAggregatePayloadOut : IAggregatePayloadCommon =>
        Payload is TAggregatePayloadOut payloadOut &&
        (Payload.GetType().Name == typeof(TAggregatePayloadOut).Name ||
            typeof(TAggregatePayload).GetBaseAggregatePayloadTypeFromAggregate().Name ==
            typeof(TAggregatePayloadOut).Name)
            ? new AggregateState<TAggregatePayloadOut>(this, payloadOut)
            : throw new AggregateTypeNotMatchException(typeof(TAggregatePayloadOut), Payload.GetType());
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
        var methods = GetType()
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Where(m => m.Name == nameof(ApplyEventToAggregatePayload));
        var method = methods.First(m => m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 3);
        var genericMethod = method.MakeGenericMethod(aggregatePayloadIn, aggregatePayloadOut, eventType);
        return (IAggregatePayloadCommon?)genericMethod.Invoke(this, new object[] { Payload, ev });
    }

    // ReSharper disable once ReturnTypeCanBeNotNullable
    public static TAggregatePayloadOut?
        ApplyEventToAggregatePayload<TAggregatePayloadIn, TAggregatePayloadOut, TEventPayload>(
            TAggregatePayloadIn aggregatePayload,
            Event<TEventPayload> ev) where TAggregatePayloadIn : IAggregatePayloadGeneratable<TAggregatePayloadIn>
        where TAggregatePayloadOut : IAggregatePayloadGeneratable<TAggregatePayloadOut>
        where TEventPayload : IEventPayload<TAggregatePayloadIn, TAggregatePayloadOut, TEventPayload> =>
        TEventPayload.OnEvent(aggregatePayload, ev);

    internal IEvent AddAndApplyEvent<TEventPayload>(TEventPayload eventPayload, string rootPartitionKey)
        where TEventPayload : IEventPayloadCommon
    {
        var aggregatePayloadBase = Payload.GetBaseAggregatePayloadType();
        var ev = Event<TEventPayload>.GenerateEvent(AggregateId, aggregatePayloadBase, eventPayload, rootPartitionKey);
        _ = GetAggregatePayloadWithAppliedEvent(Payload, ev) ??
            throw new SekibanEventNotImplementedException(
                $"{eventPayload.GetType().Name} Event not implemented on {GetType().Name} Aggregate");
        ev = ev with { Version = Version };
        ApplyEvent(ev);
        ev = ev with { Version = Version };
        return ev;
    }
}
