namespace Sekiban.EventSourcing.AggregateCommands;

public interface IChangeAggregateCommandHandler<T, C> : IAggregateCommandHandler where T : IAggregate where C : ChangeAggregateCommandBase<T>
{
    public Task<AggregateCommandResponse<T>> HandleAsync(AggregateCommandDocument<C> aggregateCommandDocument, T aggregate);
    C CleanupCommandIfNeeded(C command);
}
