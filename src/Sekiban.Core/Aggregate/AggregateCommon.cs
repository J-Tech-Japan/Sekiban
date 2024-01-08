using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Snapshot.Aggregate;
using Sekiban.Core.Types;
using System.Reflection;
namespace Sekiban.Core.Aggregate;

/// <summary>
///     System use Defines Common Aggregate behavior
///     Application developer usually do not need to use this class directly
/// </summary>
public abstract class AggregateCommon : IAggregate
{
    protected AggregateBasicInfo _basicInfo = new();


    public Guid AggregateId { get => _basicInfo.AggregateId; init => _basicInfo = _basicInfo with { AggregateId = value }; }

    public Guid LastEventId => _basicInfo.LastEventId;
    public string LastSortableUniqueId => _basicInfo.LastSortableUniqueId;
    public int AppliedSnapshotVersion => _basicInfo.AppliedSnapshotVersion;
    public int Version => _basicInfo.Version;
    public string RootPartitionKey => _basicInfo.RootPartitionKey;

    public abstract void ApplyEvent(IEvent ev);

    public abstract string GetPayloadVersionIdentifier();
    public bool EventShouldBeApplied(IEvent ev) => ev.GetSortableUniqueId().IsLaterThanOrEqual(new SortableUniqueIdValue(LastSortableUniqueId));

    public static bool CanApplyEvent(IEvent ev) => true;

    public static UAggregate Create<UAggregate>(Guid aggregateId) where UAggregate : AggregateCommon
    {
        if (typeof(UAggregate).GetConstructor(Type.EmptyTypes) is not { } c)
        {
            throw new InvalidProgramException();
        }
        var aggregate = c.Invoke(new object[] { }) as UAggregate ?? throw new InvalidProgramException();
        aggregate._basicInfo = aggregate._basicInfo with { AggregateId = aggregateId };
        return aggregate;

        // After C# 11, possibly use static interface methods. [Future idea]
    }

    protected abstract object? GetAggregatePayloadWithAppliedEvent(object aggregatePayload, IEvent ev);

    public static IAggregatePayloadCommon CreatePayloadCommon<TAggregatePayload>() where TAggregatePayload : IAggregatePayloadCommon
    {
        if (typeof(TAggregatePayload).IsAggregateSubtypePayload())
        {
            var parentType = typeof(TAggregatePayload).GetBaseAggregatePayloadTypeFromAggregate();
            var firstAggregateType = parentType.GetFirstAggregatePayloadTypeFromAggregate();
            var method = firstAggregateType.GetMethod(
                nameof(IAggregatePayloadGeneratable<TAggregatePayload>.CreateInitialPayload),
                BindingFlags.Static | BindingFlags.Public);
            return method?.Invoke(firstAggregateType, new object?[] { null }) as IAggregatePayloadCommon ??
                throw new SekibanAggregateCreateFailedException(firstAggregateType.Name);
        }
        if (typeof(TAggregatePayload).DoesImplementingFromGenericInterfaceType(typeof(IParentAggregatePayload<,>)))
        {
            var firstAggregateType = typeof(TAggregatePayload).GetFirstAggregatePayloadTypeFromAggregate();
            var method = firstAggregateType.GetMethod(
                nameof(IAggregatePayloadGeneratable<TAggregatePayload>.CreateInitialPayload),
                BindingFlags.Static | BindingFlags.Public);
            return method?.Invoke(firstAggregateType, new object?[] { null }) as IAggregatePayloadCommon ??
                throw new SekibanAggregateCreateFailedException(firstAggregateType.FullName ?? string.Empty);
        }
        if (typeof(TAggregatePayload).IsAggregatePayloadType())
        {
            var method = typeof(TAggregatePayload).GetMethod(
                nameof(IAggregatePayloadGeneratable<TAggregatePayload>.CreateInitialPayload),
                BindingFlags.Static | BindingFlags.Public);
            return method?.Invoke(typeof(TAggregatePayload), new object?[] { null }) as IAggregatePayloadCommon ??
                throw new SekibanAggregateCreateFailedException(typeof(TAggregatePayload).FullName ?? string.Empty);
        }
        if (typeof(TAggregatePayload).IsSingleProjectionPayloadType())
        {
            var method = typeof(TAggregatePayload).GetMethod(
                nameof(ISingleProjectionPayloadGeneratable<TAggregatePayload>.CreateInitialPayload),
                BindingFlags.Static | BindingFlags.Public);
            return method?.Invoke(typeof(TAggregatePayload), new object?[] { }) as IAggregatePayloadCommon ??
                throw new SekibanAggregateCreateFailedException(typeof(TAggregatePayload).FullName ?? string.Empty);
        }
        throw new SekibanAggregateCreateFailedException(typeof(TAggregatePayload).FullName ?? string.Empty);
    }

    public static TAggregatePayload CreatePayload<TAggregatePayload>() where TAggregatePayload : IAggregatePayloadCommon
    {
        if (typeof(TAggregatePayload).IsAggregateSubtypePayload())
        {
            var parentType = typeof(TAggregatePayload).GetBaseAggregatePayloadTypeFromAggregate();
            var firstAggregateType = parentType.GetFirstAggregatePayloadTypeFromAggregate();
            var method = firstAggregateType.GetMethod(
                nameof(IAggregatePayloadGeneratable<TAggregatePayload>.CreateInitialPayload),
                BindingFlags.Static | BindingFlags.Public);
            var created = method?.Invoke(firstAggregateType, new object?[] { null });
            var converted = created is TAggregatePayload payload ? payload : default;
            if (converted is not null)
            {
                return converted;
            }
            var instantiated = Activator.CreateInstance(typeof(TAggregatePayload), new object?[] { });
            return instantiated is TAggregatePayload payload2
                ? payload2
                : throw new SekibanAggregateCreateFailedException(typeof(TAggregatePayload).FullName ?? string.Empty);
        }
        if (typeof(TAggregatePayload).IsParentAggregatePayload())
        {
            var firstAggregateType = typeof(TAggregatePayload).GetFirstAggregatePayloadTypeFromAggregate();
            var method = firstAggregateType.GetMethod(
                nameof(IAggregatePayloadGeneratable<SnapshotManager>.CreateInitialPayload),
                BindingFlags.Static | BindingFlags.Public);
            var created = method?.Invoke(firstAggregateType, new object?[] { null });
            var converted = created is TAggregatePayload payload ? payload : default;
            if (converted is not null)
            {
                return converted;
            }
            var instantiated = Activator.CreateInstance(typeof(TAggregatePayload), new object?[] { });
            return instantiated is TAggregatePayload payload2
                ? payload2
                : throw new SekibanAggregateCreateFailedException(typeof(TAggregatePayload).FullName ?? string.Empty);
        }
        if (typeof(TAggregatePayload).IsAggregatePayloadType())
        {
            var method = typeof(TAggregatePayload).GetMethod(
                nameof(IAggregatePayloadGeneratable<SnapshotManager>.CreateInitialPayload),
                BindingFlags.Static | BindingFlags.Public);
            var created = method?.Invoke(typeof(TAggregatePayload), new object?[] { null });
            var converted = created is TAggregatePayload payload ? payload : default;
            if (converted is not null)
            {
                return converted;
            }
            var instantiated = Activator.CreateInstance(typeof(TAggregatePayload), new object?[] { });
            return instantiated is TAggregatePayload payload2
                ? payload2
                : throw new SekibanAggregateCreateFailedException(typeof(TAggregatePayload).FullName ?? string.Empty);
        }
        if (typeof(TAggregatePayload).IsSingleProjectionPayloadType())
        {
            var method = typeof(TAggregatePayload).GetMethod(
                nameof(ISingleProjectionPayloadGeneratable<TAggregatePayload>.CreateInitialPayload),
                BindingFlags.Static | BindingFlags.Public);
            var created = method?.Invoke(typeof(TAggregatePayload), new object?[] { });
            var converted = created is TAggregatePayload payload ? payload : default;
            if (converted is not null)
            {
                return converted;
            }
            var instantiated = Activator.CreateInstance(typeof(TAggregatePayload), new object?[] { });
            return instantiated is TAggregatePayload payload2
                ? payload2
                : throw new SekibanAggregateCreateFailedException(typeof(TAggregatePayload).FullName ?? string.Empty);
        }
        throw new SekibanAggregateCreateFailedException(typeof(TAggregatePayload).FullName ?? string.Empty);
    }
}
