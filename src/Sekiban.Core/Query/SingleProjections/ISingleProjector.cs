namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjector<out TProjectionClass> where TProjectionClass : IAggregateCommon, ISingleProjection
{
    public TProjectionClass CreateInitialAggregate(Guid aggregateId);
    public Type OriginalAggregateType();
    public string GetPayloadVersionIdentifier()
    {
        var obj = CreateInitialAggregate(Guid.Empty);
        return obj.GetPayloadVersionIdentifier();
    }
}
