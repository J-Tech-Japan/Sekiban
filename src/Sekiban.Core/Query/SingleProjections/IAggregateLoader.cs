using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
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
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<AggregateState<TAggregatePayload>?> AsDefaultStateFromInitialAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null) where TAggregatePayload : IAggregatePayloadCommon;

    /// <summary>
    ///     Get the custom projection.
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="toVersion"></param>
    /// <param name="includesSortableUniqueId"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public Task<SingleProjectionState<TSingleProjectionPayload>?> AsSingleProjectionStateAsync<TSingleProjectionPayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        string? includesSortableUniqueId = null) where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new();
    /// <summary>
    ///     Get aggregate from initial events. (without snapshot nor memory cache)
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="toVersion"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    public Task<SingleProjectionState<TSingleProjectionPayload>?> AsSingleProjectionStateFromInitialAsync<TSingleProjectionPayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null) where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new();

    /// <summary>
    ///     The normal version that uses snapshots and memory cache.
    ///     This is the default projection (default status of the aggregate).
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="toVersion"></param>
    /// <param name="includesSortableUniqueId"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<Aggregate<TAggregatePayload>?> AsAggregateAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        string? includesSortableUniqueId = null) where TAggregatePayload : IAggregatePayloadCommon;

    /// <summary>
    ///     The normal version that uses snapshots and memory cache.
    ///     This is the default projection (default status of the aggregate).
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="toVersion"></param>
    /// <param name="includesSortableUniqueId"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<AggregateState<TAggregatePayload>?> AsDefaultStateAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        string? includesSortableUniqueId = null) where TAggregatePayload : IAggregatePayloadCommon;

    /// <summary>
    ///     Get all events for target aggregate.
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="toVersion"></param>
    /// <param name="includesSortableUniqueId"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<IEnumerable<IEvent>?> AllEventsAsync<TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        string? includesSortableUniqueId = null) where TAggregatePayload : IAggregatePayloadCommon;
}
