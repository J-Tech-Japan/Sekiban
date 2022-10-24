using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using System.Collections.Immutable;
namespace Sekiban.Core.Aggregate;

public abstract class AggregateCommonBase : IAggregate
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
    public bool CanApplyEvent(IAggregateEvent ev)
    {
        return GetApplyEventAction(ev, ev.GetPayload()) is not null;
    }

    public void ApplyEvent(IAggregateEvent ev)
    {
        var action = GetApplyEventAction(ev, ev.GetPayload());
        if (action is null) { return; }
        if (ev.IsAggregateInitialEvent == false && Version == 0)
        {
            throw new SekibanInvalidEventException();
        }
        if (ev.Id == LastEventId) { return; }
        action();
        _basicInfo = _basicInfo with { LastEventId = ev.Id, LastSortableUniqueId = ev.SortableUniqueId, Version = Version + 1 };
    }
    public static UAggregate Create<UAggregate>(Guid aggregateId) where UAggregate : AggregateCommonBase
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

    protected abstract Action? GetApplyEventAction(IAggregateEvent ev, IEventPayload payload);
}
