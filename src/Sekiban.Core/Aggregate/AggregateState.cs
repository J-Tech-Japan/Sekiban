using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Types;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
namespace Sekiban.Core.Aggregate;

/// <summary>
///     Class representing the current state of the aggregate
///     This class is for the Data Transfer, not for running the aggregate
///     To make it executable, use the Aggregate class by applying this class as a snapshot
/// </summary>
/// <typeparam name="TPayload">Aggregate Payload</typeparam>
public sealed record AggregateState<TPayload> : IAggregateStateCommon where TPayload : IAggregatePayloadCommonBase
{

    public string PayloadTypeName => Payload?.GetType().Name ?? string.Empty;

    public TPayload Payload { get; init; } = CreatePayload();
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

    public AggregateState(IAggregateCommon aggregateCommon, TPayload payload) : this(aggregateCommon) => Payload = payload;

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
    public bool IsAggregatePayloadType<TAggregatePayloadExpected>() where TAggregatePayloadExpected : IAggregatePayloadCommon =>
        Payload is TAggregatePayloadExpected;

    private static TPayload CreatePayload()
    {
        if (typeof(TPayload).GetInterfaces().Any(m => m == typeof(IAggregatePayloadCommon)))
        {
            var method = typeof(TPayload).GetMethod(nameof(IAggregatePayloadCommon.CreateInitialPayload), BindingFlags.Static | BindingFlags.Public);
            var created = method?.Invoke(typeof(TPayload), new object?[] { });
            var converted = created is TPayload payload ? payload : default;
            if (converted is not null)
            {
                return converted;
            }
            var instantiated = Activator.CreateInstance(typeof(TPayload), new object?[] { });
            return instantiated is TPayload payload2 ? payload2 : throw new SekibanAggregateCreateFailedException(nameof(TPayload));
        }
        // if (typeof(TPayload).IsAggregateSubtypePayload())
        // {
        //     var parentType = typeof(TPayload).GetBaseAggregatePayloadTypeFromAggregate();
        //     var firstAggregateType = parentType.GetFirstAggregatePayloadTypeFromAggregate();
        //     var method = firstAggregateType.GetMethod(
        //         nameof(IAggregatePayloadCommon.CreateInitialPayload),
        //         BindingFlags.Static | BindingFlags.Public);
        //     var created = method?.Invoke(typeof(TPayload), new object?[] { });
        //     return created is TPayload payload ? payload : throw new SekibanAggregateCreateFailedException(nameof(TPayload));
        // }
        if (typeof(TPayload).DoesImplementingFromGenericInterfaceType(typeof(IParentAggregatePayload<,>)))
        {
            var firstAggregateType = typeof(TPayload).GetFirstAggregatePayloadTypeFromAggregate();
            var method = firstAggregateType.GetMethod(
                nameof(IAggregatePayloadCommon.CreateInitialPayload),
                BindingFlags.Static | BindingFlags.Public);
            var created = method?.Invoke(firstAggregateType, new object?[] { });
            return created is TPayload payload ? payload : throw new SekibanAggregateCreateFailedException(nameof(TPayload));
        }
        throw new SekibanAggregateCreateFailedException(nameof(TPayload));
    }

    public string GetPayloadVersionIdentifier() => Payload.GetPayloadVersionIdentifier();

    public bool GetIsDeleted() => Payload is IDeletable { IsDeleted: true };

    public dynamic GetComparableObject(AggregateState<TPayload> original, bool copyVersion = true) =>
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
