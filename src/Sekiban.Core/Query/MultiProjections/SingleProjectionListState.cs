using Sekiban.Core.Query.SingleProjections;

namespace Sekiban.Core.Query.MultiProjections;

public record SingleProjectionListState<TAggregateState> : IMultiProjectionPayloadCommon
    where TAggregateState : IAggregateCommon
{
    public IReadOnlyCollection<TAggregateState> List { get; init; } = new List<TAggregateState>();
}
