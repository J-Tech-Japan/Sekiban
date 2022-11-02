namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjectionStateConvertible<TState> where TState : IAggregateIdentifier
{
    TState ToState();
    void ApplySnapshot(TState snapshot);
}
