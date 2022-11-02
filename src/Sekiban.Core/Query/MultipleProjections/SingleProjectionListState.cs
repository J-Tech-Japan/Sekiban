using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.MultipleProjections;

public record SingleProjectionListState<TAggregateState> : IMultiProjectionPayload where TAggregateState : ISingleAggregate
{
    public IReadOnlyCollection<TAggregateState> List { get; init; } = new List<TAggregateState>();
}
