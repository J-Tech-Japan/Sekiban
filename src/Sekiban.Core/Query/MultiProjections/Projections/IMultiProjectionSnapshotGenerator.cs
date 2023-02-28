namespace Sekiban.Core.Query.MultiProjections.Projections;

public interface IMultiProjectionSnapshotGenerator
{
    Task<MultiProjectionState<TProjectionPayload>> GenerateMultiProjectionSnapshotAsync<TProjection, TProjectionPayload>(
        int minimumNumberOfEventsToGenerateSnapshot)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();

    Task<MultiProjectionState<TProjectionPayload>> GetCurrentStateAsync<TProjectionPayload>()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();
}
