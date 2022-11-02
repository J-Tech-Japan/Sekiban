namespace Sekiban.Core.Query.SingleProjections;

public interface IAggregateIdentifier : IProjection
{
    Guid AggregateId { get; }
}
