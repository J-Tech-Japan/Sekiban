namespace Sekiban.EventSourcing.AggregateCommands;

public interface IChangeAggregateCommandHandler<T, C> where T : IAggregate where C : ChangeAggregateCommandBase<T>
{
    public Task<AggregateCommandResponse<T>> HandleAsync(AggregateCommandDocument<C> aggregateCommandDocument, T aggregate);
}
