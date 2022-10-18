namespace Sekiban.Core.Query.SingleAggregate;

public interface ISingleAggregateProjector<out TProjectionClass> where TProjectionClass : ISingleAggregate, ISingleAggregateProjection
{
    public TProjectionClass CreateInitialAggregate(Guid aggregateId);
    public Type OriginalAggregateType();
}
