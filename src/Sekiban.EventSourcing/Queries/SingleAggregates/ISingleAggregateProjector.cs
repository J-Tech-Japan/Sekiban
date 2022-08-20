namespace Sekiban.EventSourcing.Queries.SingleAggregates
{
    public interface ISingleAggregateProjector<out TProjectionClass> where TProjectionClass : ISingleAggregate, ISingleAggregateProjection
    {
        public TProjectionClass CreateInitialAggregate(Guid aggregateId);
        public Type OriginalAggregateType();
    }
}
