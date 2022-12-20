namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjector<out TProjectionClass> where TProjectionClass : IAggregateCommon, ISingleProjection
{
    public TProjectionClass CreateInitialAggregate(Guid aggregateId);
    public Type GetOriginalAggregatePayloadType();
    public Type GetPayloadType();
    public string GetPayloadVersionIdentifier()
    {
        var obj = CreateInitialAggregate(Guid.Empty);
        return obj.GetPayloadVersionIdentifier();
    }
}
