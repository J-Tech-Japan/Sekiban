using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Aggregate;

/// <summary>
///     System use Aggregate Identifier
///     Application Developer does not need to implement this interface
/// </summary>
public interface IAggregate : IAggregateCommon, ISingleProjection;
