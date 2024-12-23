using Sekiban.Core.Aggregate;
using Sekiban.Core.Types;
namespace Sekiban.Core.Documents.Pools;

public record AggregateOrSingleProjectionStream<TProjection> : IAggregatesStream
{
    public AggregateContainerGroup GetAggregateContainerGroup() =>
        AggregateContainerGroupAttribute.FindAggregateContainerGroup(GetOriginalAggregateType());
    public List<string> GetStreamNames() => [GetOriginalAggregateType().Name];
    public bool GetIsAggregatePayload() => typeof(TProjection).IsAggregatePayloadType();
    private Type GetOriginalAggregateType() => GetIsAggregatePayload() switch
    {
        true => typeof(TProjection),
        false => typeof(TProjection)
            .GetAggregatePayloadTypeFromSingleProjectionPayload()
            .GetBaseAggregatePayloadTypeFromAggregate()
    };
}
