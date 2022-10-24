namespace Sekiban.Core.Query.SingleAggregate;

public interface ISingleAggregate : IProjection
{
    Guid AggregateId { get; }
}
