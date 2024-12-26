using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Documents.Pools;

public record AggregateGroupStream(
    string AggregateGroup,
    AggregateContainerGroup AggregateContainerGroup = AggregateContainerGroup.Default) : IAggregatesStream
{
    public AggregateContainerGroup GetAggregateContainerGroup() => AggregateContainerGroup;
    public List<string> GetStreamNames() => [AggregateGroup];
}
