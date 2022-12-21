using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Event;
namespace Sekiban.Core.Query.SingleProjections.Projections;

public record SingleMemoryCacheProjectionContainer<TAggregate, TState>
    where TAggregate : IAggregateCommon, SingleProjections.ISingleProjection
    where TState : IAggregateCommon
{
    public Guid AggregateId { get; init; } = Guid.Empty;
    public List<IEvent> UnsafeEvents { get; init; } = new();
    public TState? State { get; init; } = default;
    public TState? SafeState { get; init; } = default;
    public SortableUniqueIdValue? LastSortableUniqueId { get; init; } = null;
    public SortableUniqueIdValue? SafeSortableUniqueId { get; init; } = null;
}
