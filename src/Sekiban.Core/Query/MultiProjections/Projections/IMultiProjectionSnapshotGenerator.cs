using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.MultiProjections.Projections;

public interface IMultiProjectionSnapshotGenerator
{
    Task<MultiProjectionState<TProjectionPayload>> GenerateMultiProjectionSnapshotAsync<TProjection, TProjectionPayload>(
        int minimumNumberOfEventsToGenerateSnapshot,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllPartitions) where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();

    Task<MultiProjectionState<TProjectionPayload>> GenerateMultiProjectionSnapshotAsync<TProjectionPayload>(
        int minimumNumberOfEventsToGenerateSnapshot,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllPartitions) where TProjectionPayload : IMultiProjectionPayloadCommon, new() =>
        GenerateMultiProjectionSnapshotAsync<MultiProjection<TProjectionPayload>, TProjectionPayload>(
            minimumNumberOfEventsToGenerateSnapshot,
            rootPartitionKey);

    Task<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>> GenerateAggregateListSnapshotAsync<TAggregatePayload>(
        int minimumNumberOfEventsToGenerateSnapshot,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllPartitions) where TAggregatePayload : IAggregatePayloadCommon, new() =>
        GenerateMultiProjectionSnapshotAsync<SingleProjectionListProjector<Aggregate<TAggregatePayload>,
                AggregateState<TAggregatePayload>, DefaultSingleProjector<TAggregatePayload>>,
            SingleProjectionListState<AggregateState<TAggregatePayload>>>(minimumNumberOfEventsToGenerateSnapshot, rootPartitionKey);

    Task<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>
        GenerateSingleProjectionListSnapshotAsync<TSingleProjectionPayload>(
            int minimumNumberOfEventsToGenerateSnapshot,
            string rootPartitionKey = IMultiProjectionService.ProjectionAllPartitions)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new() =>
        GenerateMultiProjectionSnapshotAsync<SingleProjectionListProjector<SingleProjection<TSingleProjectionPayload>,
                SingleProjectionState<TSingleProjectionPayload>, SingleProjection<TSingleProjectionPayload>>,
            SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>(minimumNumberOfEventsToGenerateSnapshot, rootPartitionKey);

    Task<MultiProjectionState<TProjectionPayload>> GetCurrentStateAsync<TProjectionPayload>(string rootPartitionKey)
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();
}
