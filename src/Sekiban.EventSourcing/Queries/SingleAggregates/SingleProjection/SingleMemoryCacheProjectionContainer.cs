using Sekiban.EventSourcing.Documents.ValueObjects;
namespace Sekiban.EventSourcing.Queries.SingleAggregates.SingleProjection;

public record SingleMemoryCacheProjectionContainer<T, Q, P>
    where T : ISingleAggregate, ISingleAggregateProjection, ISingleAggregateProjectionDtoConvertible<Q>
    where Q : ISingleAggregate
    where P : ISingleAggregateProjector<T>, new()
{

    public Guid AggregateId { get; set; }
    public List<IAggregateEvent> UnsafeEvents { get; set; } = new();
    public Q? dto { get; set; } = default;
    public Q? safeDto { get; set; } = default;
    public SortableUniqueIdValue? LastSortableUniqueId { get; set; } = null;
    public SortableUniqueIdValue? SafeSortableUniqueId { get; set; } = null;
    public string GetCacheKey() =>
        "SingleAggregate" + typeof(T).Name + AggregateId;
}
