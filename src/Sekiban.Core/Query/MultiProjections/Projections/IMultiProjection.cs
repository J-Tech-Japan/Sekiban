using Sekiban.Core.Document.ValueObjects;
namespace Sekiban.Core.Query.MultiProjections.Projections;

public interface IMultiProjection
{
    Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionAsync<TProjection, TProjectionPayload>(
        SortableUniqueIdValue? includesSortableUniqueIdValue)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();
}
