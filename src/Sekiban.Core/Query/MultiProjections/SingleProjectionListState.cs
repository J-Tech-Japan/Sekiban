using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.MultiProjections;

/// <summary>
///     Single Projection List State.
///     It can hold either Aggregate List or Single Projection List.
/// </summary>
/// <typeparam name="TAggregateState"></typeparam>
public record SingleProjectionListState<TAggregateState> : IMultiProjectionPayloadGeneratePayload<SingleProjectionListState<TAggregateState>>
    where TAggregateState : IAggregateStateCommon
{
    public IReadOnlyCollection<TAggregateState> List { get; init; } = new List<TAggregateState>();
    public static SingleProjectionListState<TAggregateState> CreateInitialPayload() => new();
}
