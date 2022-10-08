namespace Sekiban.EventSourcing.AggregateCommands;

public interface ICreateAggregateCommandHandler<T, C> : IAggregateCommandHandler where T : IAggregate where C : ICreateAggregateCommand<T>
{
    public Task<AggregateCommandResponse<T>> HandleAsync(AggregateCommandDocument<C> aggregateCommandDocument, T aggregate);
    public Guid GenerateAggregateId(C command);
    C CleanupCommandIfNeeded(C command);
}
