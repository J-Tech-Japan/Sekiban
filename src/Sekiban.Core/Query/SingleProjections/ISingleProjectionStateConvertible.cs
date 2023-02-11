namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjectionStateConvertible<TState> where TState : IAggregateCommon
{
    bool GetPayloadTypeIs<TAggregatePayloadExpect>();
    bool GetPayloadTypeIs(Type expect);
    TState ToState();
    void ApplySnapshot(TState snapshot);
}
