using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Query.MultiProjections.Projections;

public interface IMultiProjection
{
    Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionAsync<TProjection, TProjectionPayload>(
        SortableUniqueIdValue? includesSortableUniqueIdValue)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();

    Task<MultiProjectionState<TProjectionPayload>> GetInitialMultiProjectionFromStreamAsync<TProjection, TProjectionPayload>(
        Stream stream,
        SortableUniqueIdValue? includesSortableUniqueIdValue)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();
    Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionFromMultipleStreamAsync<TProjection, TProjectionPayload>(
        Func<Task<Stream?>> stream,
        SortableUniqueIdValue? includesSortableUniqueIdValue)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();
}
