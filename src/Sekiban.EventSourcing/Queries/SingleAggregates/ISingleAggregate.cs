namespace Sekiban.EventSourcing.Queries.SingleAggregates
{
    public interface ISingleAggregate : IProjection
    {
        bool IsDeleted { get; }
        Guid AggregateId { get; }
    }
}
