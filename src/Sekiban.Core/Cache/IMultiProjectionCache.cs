using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Cache;

/// <summary>
///     defines a interface for multi projection Cache
///     Default implementation is <see cref="MultiProjectionCache" />
///     Application developer can implement this interface to provide custom cache implementation
/// </summary>
public interface IMultiProjectionCache
{
    public void Set<TProjection, TProjectionPayload>(
        string rootPartitionKey,
        MultipleMemoryProjectionContainer<TProjection, TProjectionPayload> container) where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();

    public MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>? Get<TProjection, TProjectionPayload>(string rootPartitionKey)
        where TProjection : IMultiProjector<TProjectionPayload>, new() where TProjectionPayload : IMultiProjectionPayloadCommon, new();


    public MultipleMemoryProjectionContainer<SingleProjectionListProjector<Aggregate<TAggregatePayload>,
            AggregateState<TAggregatePayload>, DefaultSingleProjector<TAggregatePayload>>,
        SingleProjectionListState<AggregateState<TAggregatePayload>>>?
        GetAggregateList<TAggregatePayload>(string rootPartitionKey) where TAggregatePayload : IAggregatePayloadCommon, new() =>
        Get<SingleProjectionListProjector<Aggregate<TAggregatePayload>, AggregateState<TAggregatePayload>, DefaultSingleProjector<TAggregatePayload>>,
            SingleProjectionListState<AggregateState<TAggregatePayload>>>(rootPartitionKey);

    public
        MultipleMemoryProjectionContainer<
            SingleProjectionListProjector<SingleProjection<TSingleProjectionPayload>, SingleProjectionState<TSingleProjectionPayload>,
                SingleProjection<TSingleProjectionPayload>>, SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>?
        GetSingleProjectionList<TSingleProjectionPayload>(string rootPartitionKey)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new() =>
        Get<SingleProjectionListProjector<SingleProjection<TSingleProjectionPayload>, SingleProjectionState<TSingleProjectionPayload>,
            SingleProjection<TSingleProjectionPayload>>, SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>(
            rootPartitionKey);
}
