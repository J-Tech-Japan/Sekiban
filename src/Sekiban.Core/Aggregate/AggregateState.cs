using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Types;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Core.Aggregate;

/// <summary>
///     Class representing the current state of the aggregate
///     This class is for the Data Transfer, not for running the aggregate
///     To make it executable, use the Aggregate class by applying this class as a snapshot
/// </summary>
/// <typeparam name="TPayload">Aggregate Payload</typeparam>
public sealed record AggregateState<TPayload> : IAggregateCommon where TPayload : IAggregatePayloadCommon
{
    public AggregateState()
    {
    }
    public AggregateState(IAggregateCommon aggregateCommon)
    {
        AggregateId = aggregateCommon.AggregateId;
        Version = aggregateCommon.Version;
        LastEventId = aggregateCommon.LastEventId;
        LastSortableUniqueId = aggregateCommon.LastSortableUniqueId;
        AppliedSnapshotVersion = aggregateCommon.AppliedSnapshotVersion;
    }

    public AggregateState(IAggregateCommon aggregateCommon, TPayload payload) : this(aggregateCommon)
    {
        Payload = payload;
    }

    public string PayloadTypeName => Payload.GetType().Name;

    public TPayload Payload { get; init; } = CreatePayload();

    [Required]
    [Description("AggregateId")]
    public Guid AggregateId { get; init; }

    [Required]
    [Description("Aggregate Version")]
    public int Version { get; init; }

    [Required]
    [Description("Last Event Id")]
    public Guid LastEventId { get; init; }

    [Required]
    [Description("Applied Snapshot Version, if not applied, it is 0")]
    public int AppliedSnapshotVersion { get; init; }

    [Required]
    [Description("Last Sortable Unique Id, SortableUniqueId defines the order of events")]
    public string LastSortableUniqueId { get; init; } = string.Empty;

    public dynamic AsDynamicTypedState()
    {
        var payloadType = Payload.GetType();
        var aggregateStateType = typeof(AggregateState<>);
        var genericAggregateStateType = aggregateStateType.MakeGenericType(payloadType);
        return Activator.CreateInstance(genericAggregateStateType, this, Payload) ?? this;
    }
    public bool IsAggregatePayloadType<TAggregatePayloadExpected>() where TAggregatePayloadExpected : IAggregatePayloadCommon
    {
        return Payload is TAggregatePayloadExpected;
    }

    private static TPayload CreatePayload()
    {
        if (typeof(TPayload).IsAggregateSubtypePayload())
        {
            return (TPayload?)Activator.CreateInstance(typeof(TPayload)) ?? throw new Exception("Failed to create Aggregate Payload");
        }
        var firstAggregateType = typeof(TPayload).GetFirstAggregatePayloadTypeFromAggregate();
        var obj = Activator.CreateInstance(firstAggregateType);
        return (TPayload?)obj ?? throw new Exception("Failed to create Aggregate Payload");
    }

    public string GetPayloadVersionIdentifier()
    {
        return Payload.GetPayloadVersionIdentifier();
    }

    public bool GetIsDeleted()
    {
        return Payload is IDeletable { IsDeleted: true };
    }

    public dynamic GetComparableObject(AggregateState<TPayload> original, bool copyVersion = true)
    {
        return this with
        {
            AggregateId = original.AggregateId,
            Version = copyVersion ? original.Version : Version,
            LastEventId = original.LastEventId,
            AppliedSnapshotVersion = original.AppliedSnapshotVersion,
            LastSortableUniqueId = original.LastSortableUniqueId
        };
    }
}
