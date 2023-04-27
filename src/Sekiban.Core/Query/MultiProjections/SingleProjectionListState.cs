using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.MultiProjections;

public record SingleProjectionListState<TAggregateState> : IMultiProjectionPayloadCommon
    where TAggregateState : IAggregateStateCommon
{
    public IReadOnlyCollection<TAggregateState> List { get; init; } = new List<TAggregateState>();
}
