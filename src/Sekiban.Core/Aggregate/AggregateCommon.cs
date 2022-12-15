using Sekiban.Core.Document.ValueObjects;
using Sekiban.Core.Event;
namespace Sekiban.Core.Aggregate;

public abstract class AggregateCommon : IAggregate
{
    protected AggregateBasicInfo _basicInfo = new();

    public Guid AggregateId
    {
        get => _basicInfo.AggregateId;
        init => _basicInfo = _basicInfo with { AggregateId = value };
    }

    public Guid LastEventId => _basicInfo.LastEventId;
    public string LastSortableUniqueId => _basicInfo.LastSortableUniqueId;
    public int AppliedSnapshotVersion => _basicInfo.AppliedSnapshotVersion;
    public int Version => _basicInfo.Version;

    public bool CanApplyEvent(IEvent ev) => GetApplyEventAction(ev, ev.GetPayload()) is not null;

    public void ApplyEvent(IEvent ev)
    {
        var action = GetApplyEventAction(ev, ev.GetPayload());
        if (action is null)
        {
            return;
        }
        if (ev.Id == LastEventId)
        {
            return;
        }
        action();
        _basicInfo = _basicInfo with
        {
            LastEventId = ev.Id, LastSortableUniqueId = ev.SortableUniqueId, Version = Version + 1
        };
    }

    public bool EventShouldBeApplied(IEvent ev) => ev.GetSortableUniqueId().LaterThan(new SortableUniqueIdValue(LastSortableUniqueId));

    public static UAggregate Create<UAggregate>(Guid aggregateId) where UAggregate : AggregateCommon
    {
        if (typeof(UAggregate).GetConstructor(Type.EmptyTypes) is not { } c)
        {
            throw new InvalidProgramException();
        }
        var aggregate = c.Invoke(new object[] { }) as UAggregate ?? throw new InvalidProgramException();
        aggregate._basicInfo = aggregate._basicInfo with { AggregateId = aggregateId };
        return aggregate;

        // C#の将来の正式バージョンで、インターフェースに静的メソッドを定義できるようになったら、書き換える。
    }

    protected abstract Action? GetApplyEventAction(IEvent ev, IEventPayloadCommon payload);
}
