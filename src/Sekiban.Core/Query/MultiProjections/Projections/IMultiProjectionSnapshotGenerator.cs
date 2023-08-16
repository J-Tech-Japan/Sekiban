using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.MultiProjections.Projections;

/// <summary>
///     Interface for MultiProjection Snapshot Generator
/// </summary>
public interface IMultiProjectionSnapshotGenerator
{
    /// <summary>
    ///     Generate MultiProjection Snapshot
    /// </summary>
    /// <param name="minimumNumberOfEventsToGenerateSnapshot"></param>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TProjection"></typeparam>
    /// <typeparam name="TProjectionPayload"></typeparam>
    /// <returns></returns>
    Task<MultiProjectionState<TProjectionPayload>> GenerateMultiProjectionSnapshotAsync<TProjection, TProjectionPayload>(
        int minimumNumberOfEventsToGenerateSnapshot,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions) where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();
    /// <summary>
    ///     Generate MultiProjection Snapshot for MultiProjection Payload
    /// </summary>
    /// <param name="minimumNumberOfEventsToGenerateSnapshot"></param>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TProjectionPayload"></typeparam>
    /// <returns></returns>
    Task<MultiProjectionState<TProjectionPayload>> GenerateMultiProjectionSnapshotAsync<TProjectionPayload>(
        int minimumNumberOfEventsToGenerateSnapshot,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions)
        where TProjectionPayload : IMultiProjectionPayloadCommon, new() =>
        GenerateMultiProjectionSnapshotAsync<MultiProjection<TProjectionPayload>, TProjectionPayload>(
            minimumNumberOfEventsToGenerateSnapshot,
            rootPartitionKey);

    /// <summary>
    ///     Generate MultiProjection Snapshot for Aggregate Payload List
    /// </summary>
    /// <param name="minimumNumberOfEventsToGenerateSnapshot"></param>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    Task<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>> GenerateAggregateListSnapshotAsync<TAggregatePayload>(
        int minimumNumberOfEventsToGenerateSnapshot,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions) where TAggregatePayload : IAggregatePayloadCommon =>
        GenerateMultiProjectionSnapshotAsync<SingleProjectionListProjector<Aggregate<TAggregatePayload>,
                AggregateState<TAggregatePayload>, DefaultSingleProjector<TAggregatePayload>>,
            SingleProjectionListState<AggregateState<TAggregatePayload>>>(minimumNumberOfEventsToGenerateSnapshot, rootPartitionKey);

    /// <summary>
    ///     Generate MultiProjection Snapshot for Single Projection Payload List
    /// </summary>
    /// <param name="minimumNumberOfEventsToGenerateSnapshot"></param>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TSingleProjectionPayload"></typeparam>
    /// <returns></returns>
    Task<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>
        GenerateSingleProjectionListSnapshotAsync<TSingleProjectionPayload>(
            int minimumNumberOfEventsToGenerateSnapshot,
            string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        GenerateMultiProjectionSnapshotAsync<SingleProjectionListProjector<SingleProjection<TSingleProjectionPayload>,
                SingleProjectionState<TSingleProjectionPayload>, SingleProjection<TSingleProjectionPayload>>,
            SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>(minimumNumberOfEventsToGenerateSnapshot, rootPartitionKey);
    /// <summary>
    ///     Get Current State for MultiProjection Payload
    /// </summary>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TProjectionPayload"></typeparam>
    /// <returns></returns>
    Task<MultiProjectionState<TProjectionPayload>> GetCurrentStateAsync<TProjectionPayload>(string rootPartitionKey)
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();
}
