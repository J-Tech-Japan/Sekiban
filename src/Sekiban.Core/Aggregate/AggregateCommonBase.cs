﻿using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using System.Reflection;
namespace Sekiban.Core.Aggregate;

public abstract class AggregateCommonBase : IAggregate
{
    protected AggregateBasicInfo _basicInfo = new();
    public ReadOnlyCollection<IAggregateEvent> Events => _basicInfo.Events.AsReadOnly();
    public Guid AggregateId
    {
        get => _basicInfo.AggregateId;
        init => _basicInfo.AggregateId = value;
    }

    public Guid LastEventId => _basicInfo.LastEventId;
    public string LastSortableUniqueId => _basicInfo.LastSortableUniqueId;
    public int AppliedSnapshotVersion => _basicInfo.AppliedSnapshotVersion;
    public int Version => _basicInfo.Version;
    public bool IsDeleted => _basicInfo.IsDeleted;
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
        _basicInfo.LastEventId = ev.Id;
        _basicInfo.LastSortableUniqueId = ev.SortableUniqueId;
        _basicInfo.Version++;
    }
    public void ResetEventsAndSnapshots()
    {
        _basicInfo.Events.Clear();
    }

    public static UAggregate Create<UAggregate>(Guid aggregateId) where UAggregate : AggregateCommonBase
    {
        if (typeof(UAggregate).GetConstructor(Type.EmptyTypes) is ConstructorInfo c)
        {
            var aggregate = c.Invoke(new object[] { }) as UAggregate ?? throw new InvalidProgramException();
            aggregate._basicInfo.AggregateId = aggregateId;
            return aggregate;
        }

        throw new InvalidProgramException();

        // C#の将来の正式バージョンで、インターフェースに静的メソッドを定義できるようになったら、書き換える。
    }

    protected abstract Action? GetApplyEventAction(IAggregateEvent ev, IEventPayload payload);

    protected abstract void AddAndApplyEvent<TEventPayload>(TEventPayload eventPayload) where TEventPayload : IEventPayload;
}