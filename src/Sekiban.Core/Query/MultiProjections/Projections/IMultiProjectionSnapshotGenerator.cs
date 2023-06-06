using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.MultiProjections.Projections;

public interface IMultiProjectionSnapshotGenerator
{
    Task<MultiProjectionState<TProjectionPayload>> GenerateMultiProjectionSnapshotAsync<TProjection, TProjectionPayload>(
        int minimumNumberOfEventsToGenerateSnapshot) where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();

    Task<MultiProjectionState<TProjectionPayload>> GenerateMultiProjectionSnapshotAsync<TProjectionPayload>(
        int minimumNumberOfEventsToGenerateSnapshot) where TProjectionPayload : IMultiProjectionPayloadCommon, new() =>
        GenerateMultiProjectionSnapshotAsync<MultiProjection<TProjectionPayload>, TProjectionPayload>(minimumNumberOfEventsToGenerateSnapshot);

    Task<MultiProjectionState<SingleProjectionListState<AggregateState<TAggregatePayload>>>>
        GenerateAggregateListSnapshotAsync<TAggregatePayload>(int minimumNumberOfEventsToGenerateSnapshot)
        where TAggregatePayload : IAggregatePayloadCommon, new() =>
        GenerateMultiProjectionSnapshotAsync<SingleProjectionListProjector<Aggregate<TAggregatePayload>,
                AggregateState<TAggregatePayload>, DefaultSingleProjector<TAggregatePayload>>,
            SingleProjectionListState<AggregateState<TAggregatePayload>>>(minimumNumberOfEventsToGenerateSnapshot);

    Task<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>
        GenerateSingleProjectionListSnapshotAsync<TSingleProjectionPayload>(int minimumNumberOfEventsToGenerateSnapshot)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new() =>
        GenerateMultiProjectionSnapshotAsync<SingleProjectionListProjector<SingleProjection<TSingleProjectionPayload>,
                SingleProjectionState<TSingleProjectionPayload>, SingleProjection<TSingleProjectionPayload>>,
            SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>(minimumNumberOfEventsToGenerateSnapshot);


    Task<MultiProjectionState<TProjectionPayload>> GetCurrentStateAsync<TProjectionPayload>()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();
}
