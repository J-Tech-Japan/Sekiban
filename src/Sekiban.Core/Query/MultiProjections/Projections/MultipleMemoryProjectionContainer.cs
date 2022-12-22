using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
namespace Sekiban.Core.Query.MultiProjections.Projections;

public record MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>
    where TProjection : IMultiProjector<TProjectionPayload>, new()
    where TProjectionPayload : IMultiProjectionPayloadCommon, new()
{
    public List<IEvent> UnsafeEvents { get; init; } = new();
    public MultiProjectionState<TProjectionPayload>? State { get; init; } = default;
    public MultiProjectionState<TProjectionPayload>? SafeState { get; init; } = default;
    public SortableUniqueIdValue? LastSortableUniqueId { get; init; } = null;
    public SortableUniqueIdValue? SafeSortableUniqueId { get; init; } = null;
}
