namespace Sekiban.Core.Query.SingleAggregate;

public interface ISingleAggregateProjectionStateConvertible<TState> where TState : ISingleAggregate
{
    TState ToState();
    void ApplySnapshot(TState snapshot);
}
