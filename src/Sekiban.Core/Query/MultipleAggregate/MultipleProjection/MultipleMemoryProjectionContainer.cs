using Sekiban.Core.Document.ValueObjects;
using Sekiban.Core.Event;
namespace Sekiban.Core.Query.MultipleAggregate.MultipleProjection;

public class MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>
    where TProjection : IMultipleAggregateProjector<TProjectionPayload>, new()
    where TProjectionPayload : IMultipleAggregateProjectionPayload, new()
{
    public List<IAggregateEvent> UnsafeEvents { get; set; } = new();
    public MultipleAggregateProjectionState<TProjectionPayload>? State { get; set; } = default;
    public MultipleAggregateProjectionState<TProjectionPayload>? SafeState { get; set; } = default;
    public SortableUniqueIdValue? LastSortableUniqueId { get; set; } = null;
    public SortableUniqueIdValue? SafeSortableUniqueId { get; set; } = null;
}
