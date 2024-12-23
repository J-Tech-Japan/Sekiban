using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Aggregate;

/// <summary>
///     Common Aggregate Interface
///     Note: Developer does not need to implement this interface.
///     It will be used with AggregateState and SingleProjectionState
/// </summary>
public interface IAggregateStateCommon : IAggregateCommon
{
    public dynamic GetPayload();
}
