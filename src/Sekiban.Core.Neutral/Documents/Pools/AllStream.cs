using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Documents.Pools;

public record AllStream(AggregateContainerGroup AggregateContainerGroup = AggregateContainerGroup.Default)
    : IAggregatesStream
{
    public AggregateContainerGroup GetAggregateContainerGroup() => AggregateContainerGroup;
    public List<string> GetStreamNames() => [];
}
