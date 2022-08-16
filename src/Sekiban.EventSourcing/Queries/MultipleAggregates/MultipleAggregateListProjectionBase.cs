namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public abstract class MultipleAggregateListProjectionBase<TProjection, TRecord> : MultipleAggregateProjectionBase<TProjection>
    where TProjection : MultipleAggregateListProjectionBase<TProjection, TRecord>, IMultipleAggregateProjectionDto where TRecord : new()
{
    public List<TRecord> Records { get; set; } = new();
    public abstract override TProjection ToDto();
    protected abstract override Action? GetApplyEventAction(IAggregateEvent ev);
    protected abstract override void CopyPropertiesFromSnapshot(TProjection snapshot);
}
