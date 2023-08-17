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
    /// <summary>
    ///     Set a projection container to cache
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <param name="container"></param>
    /// <typeparam name="TProjection"></typeparam>
    /// <typeparam name="TProjectionPayload"></typeparam>
    public void Set<TProjection, TProjectionPayload>(
        string rootPartitionKey,
        MultipleMemoryProjectionContainer<TProjection, TProjectionPayload> container) where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon;

    /// <summary>
    ///     Get a projection container from cache
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TProjection"></typeparam>
    /// <typeparam name="TProjectionPayload"></typeparam>
    /// <returns></returns>
    public MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>? Get<TProjection, TProjectionPayload>(string rootPartitionKey)
        where TProjection : IMultiProjector<TProjectionPayload>, new() where TProjectionPayload : IMultiProjectionPayloadCommon;

    /// <summary>
    ///     Get aggregate list from cache
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public MultipleMemoryProjectionContainer<SingleProjectionListProjector<Aggregate<TAggregatePayload>,
            AggregateState<TAggregatePayload>, DefaultSingleProjector<TAggregatePayload>>,
        SingleProjectionListState<AggregateState<TAggregatePayload>>>?
        GetAggregateList<TAggregatePayload>(string rootPartitionKey) where TAggregatePayload : IAggregatePayloadCommon =>
        Get<SingleProjectionListProjector<Aggregate<TAggregatePayload>, AggregateState<TAggregatePayload>, DefaultSingleProjector<TAggregatePayload>>,
            SingleProjectionListState<AggregateState<TAggregatePayload>>>(rootPartitionKey);

    /// <summary>
    ///     Get single projection list from cache
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public
        MultipleMemoryProjectionContainer<
            SingleProjectionListProjector<SingleProjection<TSingleProjectionPayload>, SingleProjectionState<TSingleProjectionPayload>,
                SingleProjection<TSingleProjectionPayload>>, SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>?
        GetSingleProjectionList<TSingleProjectionPayload>(string rootPartitionKey)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        Get<SingleProjectionListProjector<SingleProjection<TSingleProjectionPayload>, SingleProjectionState<TSingleProjectionPayload>,
            SingleProjection<TSingleProjectionPayload>>, SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>(
            rootPartitionKey);
}
