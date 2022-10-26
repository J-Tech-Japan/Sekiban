using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Query.MultipleAggregate;

public record SingleAggregateListProjectionState<TAggregateState> : IMultipleAggregateProjectionPayload where TAggregateState : ISingleAggregate
{
    public IReadOnlyCollection<TAggregateState> List { get; init; } = new List<TAggregateState>();
}
