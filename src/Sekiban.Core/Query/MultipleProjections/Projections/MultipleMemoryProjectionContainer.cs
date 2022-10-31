using Sekiban.Core.Document.ValueObjects;
using Sekiban.Core.Event;
namespace Sekiban.Core.Query.MultipleProjections.Projections;

public class MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>
    where TProjection : IMultiProjector<TProjectionPayload>, new()
    where TProjectionPayload : IMultiProjectionPayload, new()
{
    public List<IAggregateEvent> UnsafeEvents { get; set; } = new();
    public MultiProjectionState<TProjectionPayload>? State { get; set; } = default;
    public MultiProjectionState<TProjectionPayload>? SafeState { get; set; } = default;
    public SortableUniqueIdValue? LastSortableUniqueId { get; set; } = null;
    public SortableUniqueIdValue? SafeSortableUniqueId { get; set; } = null;
}
