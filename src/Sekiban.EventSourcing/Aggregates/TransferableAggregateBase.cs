using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Aggregates;

public abstract class TransferableAggregateBase<TContents> : AggregateBase, ISingleAggregateProjectionDtoConvertible<AggregateDto<TContents>>
    where TContents : IAggregateContents, new()
{
    protected TContents Contents { get; set; } = new();

    public AggregateDto<TContents> ToDto() =>
        new(this, Contents);

    public void ApplySnapshot(AggregateDto<TContents> snapshot)
    {
        _basicInfo.Version = snapshot.Version;
        _basicInfo.LastEventId = snapshot.LastEventId;
        _basicInfo.LastSortableUniqueId = snapshot.LastSortableUniqueId;
        _basicInfo.AppliedSnapshotVersion = snapshot.Version;
        _basicInfo.IsDeleted = snapshot.IsDeleted;
        CopyPropertiesFromSnapshot(snapshot);
    }

    protected sealed override void AddAndApplyEvent<TEventPayload>(TEventPayload eventPayload)
    {
        var ev = eventPayload is ICreatedEventPayload
            ? AggregateEvent<TEventPayload>.CreatedEvent(AggregateId, eventPayload, GetType())
            : AggregateEvent<TEventPayload>.ChangedEvent(AggregateId, eventPayload, GetType());

        if (GetApplyEventAction(ev, eventPayload) == null)
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
