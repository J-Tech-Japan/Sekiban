using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.MultiProjections.Projections;

public interface IMultiProjectionSnapshotGenerator
{
    Task<MultiProjectionState<TProjectionPayload>> GenerateMultiProjectionSnapshotAsync<TProjection, TProjectionPayload>(
        string? rootPartitionKey,
        int minimumNumberOfEventsToGenerateSnapshot) where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();

    Task<MultiProjectionState<TProjectionPayload>> GenerateMultiProjectionSnapshotAsync<TProjectionPayload>(
        string? rootPartitionKey,
        int minimumNumberOfEventsToGenerateSnapshot) where TProjectionPayload : IMultiProjectionPayloadCommon, new() =>
        GenerateMultiProjectionSnapshotAsync<MultiProjection<TProjectionPayload>, TProjectionPayload>(
            rootPartitionKey,
            minimumNumberOfEventsToGenerateSnapshot);

    Task<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>>
        GenerateAggregateListSnapshotAsync<TAggregatePayload>(string? rootPartitionKey, int minimumNumberOfEventsToGenerateSnapshot)
        where TAggregatePayload : IAggregatePayloadCommon, new() =>
        GenerateMultiProjectionSnapshotAsync<SingleProjectionListProjector<Aggregate<TAggregatePayload>,
                AggregateState<TAggregatePayload>, DefaultSingleProjector<TAggregatePayload>>,
            SingleProjectionListState<AggregateState<TAggregatePayload>>>(rootPartitionKey, minimumNumberOfEventsToGenerateSnapshot);

    Task<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>
        GenerateSingleProjectionListSnapshotAsync<TSingleProjectionPayload>(string? rootPartitionKey, int minimumNumberOfEventsToGenerateSnapshot)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new() =>
        GenerateMultiProjectionSnapshotAsync<SingleProjectionListProjector<SingleProjection<TSingleProjectionPayload>,
                SingleProjectionState<TSingleProjectionPayload>, SingleProjection<TSingleProjectionPayload>>,
            SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>(rootPartitionKey, minimumNumberOfEventsToGenerateSnapshot);

    Task<MultiProjectionState<TProjectionPayload>> GetCurrentStateAsync<TProjectionPayload>(string? rootPartitionKey)
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();
}
