using Sekiban.Core.Aggregate;
using Sekiban.Core.Types;
namespace Sekiban.Core.Documents.Pools;

public record AggregateWriteStream(Type AggregatePayloadType) : IWriteDocumentStream
{
    public AggregateContainerGroup GetAggregateContainerGroup() =>
        AggregateContainerGroupAttribute.FindAggregateContainerGroup(GetOriginal());
    public Type GetOriginal() => AggregatePayloadType.IsAggregatePayloadType()
        ? AggregatePayloadType.GetBaseAggregatePayloadTypeFromAggregate()
        : AggregatePayloadType;
}
