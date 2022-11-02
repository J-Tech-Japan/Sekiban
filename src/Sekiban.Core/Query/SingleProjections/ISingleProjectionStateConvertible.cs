namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjectionStateConvertible<TState> where TState : IAggregateCommon
{
    TState ToState();
    void ApplySnapshot(TState snapshot);
}
