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

    protected sealed override void AddAndApplyEvent(IAggregateEvent ev)
    {
        if (GetApplyEventAction(ev) == null)
        {
            throw new SekibanEventNotImplementedException();
        }

        // Add Event
        _events.Add(ev);

        // Apply Event
        ApplyEvent(ev);
        ev.SetVersion(Version);
    }
    protected void CopyPropertiesFromSnapshot(AggregateDto<TContents> snapshot)
    {
        Contents = snapshot.Contents;
    }
}
