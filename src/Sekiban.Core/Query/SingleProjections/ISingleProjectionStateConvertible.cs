namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjectionStateConvertible<TState> where TState : ISingleAggregate
{
    TState ToState();
    void ApplySnapshot(TState snapshot);
}
