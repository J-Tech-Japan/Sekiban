using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Aggregate;

public abstract class AggregateBase<TContents> : AggregateCommonBase, ISingleAggregateProjectionDtoConvertible<AggregateDto<TContents>>
    where TContents : IAggregateContents, new()
{
    protected TContents Contents { get; private set; } = new();
    private new bool IsDeleted { get => _basicInfo.IsDeleted; set => _basicInfo.IsDeleted = value; }
    public AggregateDto<TContents> ToDto()
    {
        return new AggregateDto<TContents>(this, Contents);
    }

    public void ApplySnapshot(AggregateDto<TContents> snapshot)
    {
        _basicInfo.Version = snapshot.Version;
        _basicInfo.LastEventId = snapshot.LastEventId;
        _basicInfo.LastSortableUniqueId = snapshot.LastSortableUniqueId;
        _basicInfo.AppliedSnapshotVersion = snapshot.Version;
        _basicInfo.IsDeleted = snapshot.IsDeleted;
        CopyPropertiesFromSnapshot(snapshot);
    }

    protected override Action? GetApplyEventAction(IAggregateEvent ev, IEventPayload payload)
    {
        var func = GetApplyEventFunc(ev, payload);
        return () =>
        {
            if (func == null) { return; }
            var result = func(new AggregateVariable<TContents>(Contents, IsDeleted));
            Contents = result.Contents;
            IsDeleted = result.IsDeleted;
        };
    }
    protected abstract Func<AggregateVariable<TContents>, AggregateVariable<TContents>>? GetApplyEventFunc(IAggregateEvent ev, IEventPayload payload);

    protected sealed override void AddAndApplyEvent<TEventPayload>(TEventPayload eventPayload)
    {
        var ev = eventPayload is ICreatedEventPayload
            ? AggregateEvent<TEventPayload>.CreatedEvent(AggregateId, GetType(), eventPayload)
            : AggregateEvent<TEventPayload>.ChangedEvent(AggregateId, GetType(), eventPayload);

        if (GetApplyEventAction(ev, eventPayload) is null)
        {
            throw new SekibanEventNotImplementedException();
        }
        // バージョンが変わる前に、イベントには現在のバージョンを入れて動かす
        ev = ev with { Version = Version };
        ApplyEvent(ev);
        ev = ev with { Version = Version };
        _basicInfo.Events.Add(ev);
    }
    protected void CopyPropertiesFromSnapshot(AggregateDto<TContents> snapshot)
    {
        Contents = snapshot.Contents;
    }
}