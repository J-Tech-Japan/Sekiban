namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjector<out TProjectionClass> where TProjectionClass : IAggregateIdentifier, ISingleProjection
{
    public TProjectionClass CreateInitialAggregate(Guid aggregateId);
    public Type OriginalAggregateType();
}
