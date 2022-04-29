namespace Sekiban.EventSourcing.Queries;

public interface ISingleAggregate : IProjection
{
    bool IsDeleted { get; }
    Guid AggregateId { get; }
}
