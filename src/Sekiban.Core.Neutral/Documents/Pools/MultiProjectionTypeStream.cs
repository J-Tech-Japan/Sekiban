using Sekiban.Core.Aggregate;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Documents.Pools;

public record MultiProjectionTypeStream<TProjectionPayload> : IAggregatesStream
    where TProjectionPayload : IMultiProjectionPayloadCommon
{
    public AggregateContainerGroup GetAggregateContainerGroup() =>
        AggregateContainerGroupAttribute.FindAggregateContainerGroup(typeof(TProjectionPayload));
    public List<string> GetStreamNames()
    {
        var payload = MultiProjection<TProjectionPayload>.GeneratePayload();
        var projectionPayload = payload as IMultiProjectionPayload<TProjectionPayload> ??
            throw new SekibanMultiProjectionMustInheritISingleProjectionEventApplicable();
        return projectionPayload.GetTargetAggregatePayloads().GetAggregateNames();
    }
}
public record MultiProjectionTypeStream(Type ProjectionType, IList<string> AggregateNames) : IAggregatesStream
{
    public AggregateContainerGroup GetAggregateContainerGroup() =>
        AggregateContainerGroupAttribute.FindAggregateContainerGroup(ProjectionType);
    public List<string> GetStreamNames() => AggregateNames.ToList();
}
