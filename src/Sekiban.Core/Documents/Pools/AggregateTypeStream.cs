using Sekiban.Core.Aggregate;
using Sekiban.Core.Types;
namespace Sekiban.Core.Documents.Pools;

public record AggregateTypeStream(Type AggregateType) : IAggregatesStream
{
    public AggregateContainerGroup GetAggregateContainerGroup() =>
        AggregateContainerGroupAttribute.FindAggregateContainerGroup(GetOriginalAggregateType());
    public List<string> GetStreamNames() => [GetOriginalAggregateType().Name];
    private Type GetOriginalAggregateType() => AggregateType.GetBaseAggregatePayloadTypeFromAggregate();
}
public record AggregateTypeStream<TAggregatePayload> : IAggregatesStream
    where TAggregatePayload : IAggregatePayloadCommon
{
    public AggregateContainerGroup GetAggregateContainerGroup() =>
        AggregateContainerGroupAttribute.FindAggregateContainerGroup(GetOriginalAggregateType());
    public List<string> GetStreamNames() => [GetOriginalAggregateType().Name];
    private Type GetOriginalAggregateType() => typeof(TAggregatePayload).GetBaseAggregatePayloadTypeFromAggregate();
}
