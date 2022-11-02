namespace Sekiban.Core.Query.SingleProjections;

public interface IAggregateCommon : IProjection
{
    Guid AggregateId { get; }
}
