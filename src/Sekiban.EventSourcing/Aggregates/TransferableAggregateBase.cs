using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Aggregates;

public abstract class TransferableAggregateBase<TContents> : AggregateBase, ISingleAggregateProjectionDtoConvertible<AggregateDto<TContents>>
    where TContents : IAggregateContents
{
    protected TContents Contents { get; set; }
    public TransferableAggregateBase(Guid aggregateId) : base(aggregateId) { }

    public AggregateDto<TContents> ToDto() =>
        new(this, Contents);

    public void ApplySnapshot(AggregateDto<TContents> snapshot)
    {
        Version = snapshot.Version;
        LastEventId = snapshot.LastEventId;
        LastSortableUniqueId = snapshot.LastSortableUniqueId;
        AppliedSnapshotVersion = snapshot.Version;
        IsDeleted = snapshot.IsDeleted;
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
        _events.Add(ev);
    }
    protected void CopyPropertiesFromSnapshot(AggregateDto<TContents> snapshot)
    {
        Contents = snapshot.Contents;
    }
}
