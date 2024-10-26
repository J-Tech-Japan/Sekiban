using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Types;
namespace Sekiban.Core.Documents;

public interface IPooledEventWriter : IDocumentWriter
{
}
public record EventRetrievalInfo(
    OptionalValue<string> RootPartitionKey,
    OptionalValue<IAggregatesStream> AggregateStream,
    OptionalValue<Guid> AggregateId,
    OptionalValue<SortableUniqueIdValue> SinceSortableUniqueId);
public interface IAggregatesStream
{
    public AggregateContainerGroup GetAggregateContainerGroup();
    public List<string> GetStreamNames();
}
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
public record AggregateTypeStream(Type AggregateType) : IAggregatesStream
{
    public AggregateContainerGroup GetAggregateContainerGroup() =>
        AggregateContainerGroupAttribute.FindAggregateContainerGroup(AggregateType);
    public List<string> GetStreamNames() => [AggregateType.Name];
}
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
public record AggregateTypeStream<TAggregatePayload> : IAggregatesStream
    where TAggregatePayload : IAggregatePayloadCommon
{
    public AggregateContainerGroup GetAggregateContainerGroup() =>
        AggregateContainerGroupAttribute.FindAggregateContainerGroup(GetOriginalAggregateType());
    public List<string> GetStreamNames() => [GetOriginalAggregateType().Name];
    private Type GetOriginalAggregateType() => typeof(TAggregatePayload).GetBaseAggregatePayloadTypeFromAggregate();
}
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
public record AggregateGroupStream(
    string AggregateGroup,
    AggregateContainerGroup AggregateContainerGroup = AggregateContainerGroup.Default) : IAggregatesStream
{
    public AggregateContainerGroup GetAggregateContainerGroup() => AggregateContainerGroup;
    public List<string> GetStreamNames() => [AggregateGroup];
}
