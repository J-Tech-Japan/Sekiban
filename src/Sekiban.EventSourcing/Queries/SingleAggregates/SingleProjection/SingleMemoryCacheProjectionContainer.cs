using Sekiban.EventSourcing.Documents.ValueObjects;
namespace Sekiban.EventSourcing.Queries.SingleAggregates.SingleProjection
{
    public record SingleMemoryCacheProjectionContainer<TAggregate, TDto> where TAggregate : ISingleAggregate, ISingleAggregateProjection
        where TDto : ISingleAggregate
    {
        public Guid AggregateId { get; set; }
        public List<IAggregateEvent> UnsafeEvents { get; set; } = new();
        public TDto? Dto { get; set; } = default;
        public TDto? SafeDto { get; set; } = default;
        public SortableUniqueIdValue? LastSortableUniqueId { get; set; } = null;
        public SortableUniqueIdValue? SafeSortableUniqueId { get; set; } = null;
        public SingleMemoryCacheProjectionContainer(Guid aggregateId) =>
            AggregateId = aggregateId;
    }
}
