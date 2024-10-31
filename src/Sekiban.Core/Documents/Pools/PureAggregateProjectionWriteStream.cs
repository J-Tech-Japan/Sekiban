using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Documents.Pools;

public record PureAggregateProjectionWriteStream(Type PureAggregateProjectionType) : IWriteDocumentStream
{
    public AggregateContainerGroup GetAggregateContainerGroup() =>
        AggregateContainerGroupAttribute.FindAggregateContainerGroup(PureAggregateProjectionType);
}
