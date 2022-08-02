using Sekiban.EventSourcing.Documents.ValueObjects;
namespace Sekiban.EventSourcing.Queries.MultipleAggregates.MultipleProjection;

public class MultipleMemoryProjectionContainer<P, Q> where P : IMultipleAggregateProjector<Q>, new() where Q : IMultipleAggregateProjectionDto, new()
{
    public List<IAggregateEvent> UnsafeEvents { get; set; } = new();
    public Q? dto { get; set; } = default;
    public Q? safeDto { get; set; } = default;
    public SortableUniqueIdValue? LastSortableUniqueId { get; set; } = null;
    public SortableUniqueIdValue? SafeSortableUniqueId { get; set; } = null;
}
