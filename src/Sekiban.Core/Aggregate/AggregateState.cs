using Sekiban.Core.Query.SingleProjections;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Core.Aggregate;

/// <summary>
///     Class representing the current state of the aggregate
///     This class is for the Data Transfer, not for running the aggregate
///     To make it executable, use the Aggregate class by applying this class as a snapshot
/// </summary>
/// <typeparam name="TPayload">Aggregate Payload</typeparam>
public sealed record AggregateState<TPayload> : IAggregateStateCommon where TPayload : IAggregatePayloadCommon
{

    public string PayloadTypeName => Payload.GetType().Name;

    public TPayload Payload { get; init; } = AggregateCommon.CreatePayload<TPayload>();
    public bool IsNew => Version == 0;
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
        RootPartitionKey = aggregateCommon.RootPartitionKey;
    }

    public AggregateState(IAggregateCommon aggregateCommon, TPayload payload) : this(aggregateCommon) =>
        Payload = payload;

    [Required]
    [Description("AggregateId")]
    public Guid AggregateId { get; init; }

    [Required]
    [Description("Aggregate Version")]
    public int Version { get; init; }

    [Required]
    [Description("Root Partition Key")]
    public string RootPartitionKey { get; init; } = string.Empty;

    [Required]
    [Description("Last Event Id")]
    public Guid LastEventId { get; init; }

    [Required]
    [Description("Applied Snapshot Version, if not applied, it is 0")]
    public int AppliedSnapshotVersion { get; init; }

    [Required]
    [Description("Last Sortable Unique Id, SortableUniqueId defines the order of events")]
    public string LastSortableUniqueId { get; init; } = string.Empty;
    public dynamic GetPayload() => Payload;

    public dynamic AsDynamicTypedState()
    {
        var payloadType = Payload.GetType();
        var aggregateStateType = typeof(AggregateState<>);
        var genericAggregateStateType = aggregateStateType.MakeGenericType(payloadType);
        return Activator.CreateInstance(genericAggregateStateType, this, Payload) ?? this;
    }
    public bool IsAggregatePayloadType<TAggregatePayloadExpected>()
        where TAggregatePayloadExpected : IAggregatePayloadCommon =>
        Payload is TAggregatePayloadExpected;

    public string GetPayloadVersionIdentifier() => Payload.GetPayloadVersionIdentifier();

    public bool GetIsDeleted() => Payload is IDeletable { IsDeleted: true };
    public dynamic GetComparableObject(IAggregateStateCommon original, bool copyVersion = true) =>
        this with
        {
            AggregateId = original.AggregateId,
            Version = copyVersion ? original.Version : Version,
            LastEventId = original.LastEventId,
            AppliedSnapshotVersion = original.AppliedSnapshotVersion,
            LastSortableUniqueId = original.LastSortableUniqueId,
            RootPartitionKey = original.RootPartitionKey
        };
}
