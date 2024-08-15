using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
namespace Sekiban.Core.Query.SingleProjections;

/// <summary>
///     Aggregate Loader Interface.
///     Developers can use this interface to load the aggregate.
/// </summary>
public interface IAggregateLoader
{
    /// <summary>
    ///     Creates an Aggregate from the initial event without using the memory cache.
    ///     It's slow, so please normally use the cached version.
    ///     This remains for testing and verification purposes.
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="toVersion"></param>
    /// <param name="retrievalOptions"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<AggregateState<TAggregatePayload>?> AsDefaultStateFromInitialAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null) where TAggregatePayload : IAggregatePayloadCommon;
    /// <summary>
    ///     Creates an Aggregate from the initial event without using the memory cache.
    ///     If aggregate does not exist, it returns SekibanAggregateNotExistsException.
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    /// <exception cref="SekibanAggregateNotExistsException"></exception>
    public async Task<ResultBox<AggregateState<TAggregatePayload>>>
        AsDefaultStateFromInitialWithResultAsync<TAggregatePayload>(
            Guid aggregateId,
            string rootPartitionKey = IDocument.DefaultRootPartitionKey,
            int? toVersion = null) where TAggregatePayload : IAggregatePayloadCommon =>
        await ResultBox.WrapTry(
            async () => await AsDefaultStateFromInitialAsync<TAggregatePayload>(
                    aggregateId,
                    rootPartitionKey,
                    toVersion) switch
                {
                    { } state => state,
                    null => throw new SekibanAggregateNotExistsException(
                        aggregateId,
                        typeof(TAggregatePayload).Name,
                        rootPartitionKey)
                });


    /// <summary>
    ///     Get the custom projection.
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="toVersion"></param>
    /// <param name="retrievalOptions"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public Task<SingleProjectionState<TSingleProjectionPayload>?>
        AsSingleProjectionStateAsync<TSingleProjectionPayload>(
            Guid aggregateId,
            string rootPartitionKey = IDocument.DefaultRootPartitionKey,
            int? toVersion = null,
            SingleProjectionRetrievalOptions? retrievalOptions = null)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;
    /// <summary>
    ///     Get aggregate from initial events. (without snapshot nor memory cache)
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public Task<SingleProjectionState<TSingleProjectionPayload>?>
        AsSingleProjectionStateFromInitialAsync<TSingleProjectionPayload>(
            Guid aggregateId,
            string rootPartitionKey = IDocument.DefaultRootPartitionKey,
            int? toVersion = null) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;

    /// <summary>
    ///     The normal version that uses snapshots and memory cache.
    ///     This is the default projection (default status of the aggregate).
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="toVersion"></param>
    /// <param name="retrievalOptions"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<Aggregate<TAggregatePayload>?> AsAggregateAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        SingleProjectionRetrievalOptions? retrievalOptions = null) where TAggregatePayload : IAggregatePayloadCommon;

    /// <summary>
    ///     The normal version that uses snapshots and memory cache.
    ///     This is the default projection (default status of the aggregate).
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="toVersion"></param>
    /// <param name="retrievalOptions"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<AggregateState<TAggregatePayload>?> AsDefaultStateAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        SingleProjectionRetrievalOptions? retrievalOptions = null) where TAggregatePayload : IAggregatePayloadCommon;

    public async Task<ResultBox<AggregateState<TAggregatePayload>>> AsDefaultStateWithResultAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        SingleProjectionRetrievalOptions? retrievalOptions = null) where TAggregatePayload : IAggregatePayloadCommon =>
        await ResultBox.WrapTry(
            async () => await AsDefaultStateAsync<TAggregatePayload>(
                    aggregateId,
                    rootPartitionKey,
                    toVersion,
                    retrievalOptions) switch
                {
                    { } state => state,
                    null => throw new SekibanAggregateNotExistsException(
                        aggregateId,
                        typeof(TAggregatePayload).Name,
                        rootPartitionKey)
                });

    /// <summary>
    ///     Get all events for target aggregate.
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<IEnumerable<IEvent>?> AllEventsAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null) where TAggregatePayload : IAggregatePayloadCommon;
}
