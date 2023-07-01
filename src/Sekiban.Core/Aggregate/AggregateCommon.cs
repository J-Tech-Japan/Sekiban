using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
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

    public bool CanApplyEvent(IEvent ev) => true;

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
}
