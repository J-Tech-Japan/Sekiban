using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

/// <summary>
///     Single projection internal interface. Developer does not need to implement this interface.
///     Shows that the state can be converted to the aggregate.
/// </summary>
/// <typeparam name="TState"></typeparam>
public interface ISingleProjectionStateConvertible<TState> where TState : IAggregateCommon
{
    bool GetPayloadTypeIs<TAggregatePayloadExpect>();
    bool GetPayloadTypeIs(Type expect);
    TState ToState();
    bool CanApplySnapshot(IAggregateStateCommon? snapshot);
    void ApplySnapshot(IAggregateStateCommon snapshot);
}
