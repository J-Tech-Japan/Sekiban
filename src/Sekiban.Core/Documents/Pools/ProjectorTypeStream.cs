using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Types;
namespace Sekiban.Core.Documents.Pools;

public class ProjectorTypeStream<TProjectionPayload> : IAggregatesStream
    where TProjectionPayload : ISingleProjectionPayloadCommon
{
    public AggregateContainerGroup GetAggregateContainerGroup() =>
        AggregateContainerGroupAttribute.FindAggregateContainerGroup(GetOriginalAggregateType());
    public List<string> GetStreamNames() => [GetOriginalAggregateType().Name];
    private Type GetOriginalAggregateType() => typeof(TProjectionPayload)
        .GetAggregatePayloadTypeFromSingleProjectionPayload()
        .GetBaseAggregatePayloadTypeFromAggregate();
}
