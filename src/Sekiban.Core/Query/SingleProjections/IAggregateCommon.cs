namespace Sekiban.Core.Query.SingleProjections;

/// <summary>
///     Aggregate Common Interface.
///     Aggregate developer does not need to implement this interface directly.
/// </summary>
public interface IAggregateCommon : IProjection
{
    Guid AggregateId { get; }
}
