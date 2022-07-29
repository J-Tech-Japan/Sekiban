namespace Sekiban.EventSourcing.AggregateCommands;

public abstract class CreateAggregateCommandHandlerBase<T, C> : ICreateAggregateCommandHandler<T, C>
    where T : IAggregate where C : ICreateAggregateCommand<T>, new()
{

    async Task<AggregateCommandResponse<T>> ICreateAggregateCommandHandler<T, C>.HandleAsync(AggregateCommandDocument<C> command, T aggregate)
    {
        await ExecCreateCommandAsync(aggregate, command.Payload);
        return await Task.FromResult(new AggregateCommandResponse<T>(aggregate));
    }
    public abstract Guid GenerateAggregateId(C command);

    protected abstract Task ExecCreateCommandAsync(T aggregate, C command);
}
