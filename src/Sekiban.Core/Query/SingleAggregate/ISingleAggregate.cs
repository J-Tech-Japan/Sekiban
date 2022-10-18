namespace Sekiban.Core.Query.SingleAggregate;

public interface ISingleAggregate : IProjection
{
    bool IsDeleted { get; }
    Guid AggregateId { get; }
}
