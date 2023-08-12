using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

/// <summary>
///     Single projection Projector interface.
/// </summary>
/// <typeparam name="TProjectionClass"></typeparam>
public interface ISingleProjector<TProjectionClass> where TProjectionClass : IAggregateCommon, ISingleProjection
{
    public TProjectionClass CreateInitialAggregate(Guid aggregateId);
    public TProjectionClass CreateAggregateFromState(TProjectionClass current, object state, SekibanAggregateTypes sekibanAggregateTypes);
    public Type GetOriginalAggregatePayloadType();
    public Type GetPayloadType();
    public string GetPayloadVersionIdentifier()
    {
        var obj = CreateInitialAggregate(Guid.Empty);
        return obj.GetPayloadVersionIdentifier();
    }
}
