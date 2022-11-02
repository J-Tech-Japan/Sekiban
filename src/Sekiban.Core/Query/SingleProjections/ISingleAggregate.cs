namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleAggregate : IProjection
{
    Guid AggregateId { get; }
}
