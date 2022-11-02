using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.MultipleProjections;

public record SingleProjectionListState<TAggregateState> : IMultiProjectionPayload where TAggregateState : IAggregateIdentifier
{
    public IReadOnlyCollection<TAggregateState> List { get; init; } = new List<TAggregateState>();
}
