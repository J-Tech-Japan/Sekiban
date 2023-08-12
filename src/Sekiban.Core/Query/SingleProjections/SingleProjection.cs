using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Types;
namespace Sekiban.Core.Query.SingleProjections;

/// <summary>
///     Single Projection Implementation.
///     Developer does not need to use this class directly.
/// </summary>
/// <typeparam name="TProjectionPayload"></typeparam>
public class SingleProjection<TProjectionPayload> : ISingleProjection, ISingleProjectionStateConvertible<SingleProjectionState<TProjectionPayload>>,
    IAggregateCommon, ISingleProjector<SingleProjection<TProjectionPayload>> where TProjectionPayload : ISingleProjectionPayloadCommon, new()
{
    public TProjectionPayload Payload { get; set; } = new();
    public Guid LastEventId { get; set; }
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public int AppliedSnapshotVersion { get; set; }
    public int Version { get; set; }
    public string RootPartitionKey { get; set; } = string.Empty;
    public Guid AggregateId { get; init; }
    public string GetPayloadVersionIdentifier() => Payload.GetPayloadVersionIdentifier();
    public bool EventShouldBeApplied(IEvent ev) => ev.GetSortableUniqueId().IsLaterThanOrEqual(new SortableUniqueIdValue(LastSortableUniqueId));

    public void ApplyEvent(IEvent ev)
    {
        if (ev.Id == LastEventId)
        {
            return;
        }
        (ev, var payload) = EventHelper.GetConvertedEventAndPayloadIfConverted(ev, ev.GetPayload());
        if (payload is UnregisteredEventPayload || payload is EmptyEventPayload)
        {
            return;
        }
        var method = typeof(TProjectionPayload).GetMethod(nameof(ApplyEvent));
        var genericMethod = method?.MakeGenericMethod(ev.GetEventPayloadType());
        Payload = (TProjectionPayload)(genericMethod?.Invoke(typeof(TProjectionPayload), new object[] { Payload, ev }) ?? Payload);
        LastEventId = ev.Id;
        LastSortableUniqueId = ev.SortableUniqueId;
        RootPartitionKey = ev.RootPartitionKey;
        Version++;
    }

    public bool GetPayloadTypeIs<TAggregatePayloadExpect>() => Payload is TAggregatePayloadExpect;
    public bool GetPayloadTypeIs(Type expect)
    {
        if (!expect.IsAggregatePayloadType()) { return false; }
        var method = GetType().GetMethods().FirstOrDefault(m => m.Name == nameof(GetPayloadTypeIs) && m.IsGenericMethod);
        var genericMethod = method?.MakeGenericMethod(expect);
        return (bool?)genericMethod?.Invoke(this, null) ?? false;
    }
    public SingleProjectionState<TProjectionPayload> ToState() =>
        new(
            Payload,
            AggregateId,
            LastEventId,
            LastSortableUniqueId,
            AppliedSnapshotVersion,
            Version,
            RootPartitionKey);
    public bool CanApplySnapshot(IAggregateStateCommon? snapshot) => snapshot is not null && snapshot.GetPayload() is TProjectionPayload;
    public void ApplySnapshot(IAggregateStateCommon snapshot)
    {
        Version = snapshot.Version;
        LastEventId = snapshot.LastEventId;
        LastSortableUniqueId = snapshot.LastSortableUniqueId;
        AppliedSnapshotVersion = snapshot.Version;
        RootPartitionKey = snapshot.RootPartitionKey;
        Payload = snapshot.GetPayload();
    }

    public Type GetPayloadType() => typeof(TProjectionPayload);

    public SingleProjection<TProjectionPayload> CreateInitialAggregate(Guid aggregateId) => new() { AggregateId = aggregateId };
    public SingleProjection<TProjectionPayload> CreateAggregateFromState(
        SingleProjection<TProjectionPayload> current,
        object state,
        SekibanAggregateTypes sekibanAggregateTypes) =>
        throw new NotImplementedException();

    public Type GetOriginalAggregatePayloadType() =>
        typeof(TProjectionPayload).GetAggregatePayloadTypeFromSingleProjectionPayload().GetBaseAggregatePayloadTypeFromAggregate();
    public bool CanApplyEvent(IEvent ev) => true;


    public bool GetIsDeleted() => Payload is IDeletable { IsDeleted: true };
}
