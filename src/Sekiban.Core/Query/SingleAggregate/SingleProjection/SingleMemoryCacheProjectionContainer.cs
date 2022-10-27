using Sekiban.Core.Document.ValueObjects;
using Sekiban.Core.Event;
namespace Sekiban.Core.Query.SingleAggregate.SingleProjection;

public record SingleMemoryCacheProjectionContainer<TAggregate, TState> where TAggregate : ISingleAggregate, ISingleAggregateProjection
    where TState : ISingleAggregate
{
    public SingleMemoryCacheProjectionContainer(Guid aggregateId)
    {
        AggregateId = aggregateId;
    }
    public Guid AggregateId { get; set; }
    public List<IAggregateEvent> UnsafeEvents { get; set; } = new();
    public TState? State { get; set; } = default;
    public TState? SafeState { get; set; } = default;
    public SortableUniqueIdValue? LastSortableUniqueId { get; set; } = null;
    public SortableUniqueIdValue? SafeSortableUniqueId { get; set; } = null;
}
