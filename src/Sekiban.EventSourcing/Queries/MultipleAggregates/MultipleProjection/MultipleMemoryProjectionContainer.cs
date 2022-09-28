using Sekiban.EventSourcing.Documents.ValueObjects;
namespace Sekiban.EventSourcing.Queries.MultipleAggregates.MultipleProjection;

public class MultipleMemoryProjectionContainer<TProjection, TProjectionContents>
    where TProjection : IMultipleAggregateProjector<TProjectionContents>, new()
    where TProjectionContents : IMultipleAggregateProjectionContents, new()
{
    public List<IAggregateEvent> UnsafeEvents { get; set; } = new();
    public MultipleAggregateProjectionContentsDto<TProjectionContents>? Dto { get; set; } = default;
    public MultipleAggregateProjectionContentsDto<TProjectionContents>? SafeDto { get; set; } = default;
    public SortableUniqueIdValue? LastSortableUniqueId { get; set; } = null;
    public SortableUniqueIdValue? SafeSortableUniqueId { get; set; } = null;
}
