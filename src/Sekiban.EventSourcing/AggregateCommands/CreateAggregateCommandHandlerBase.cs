namespace Sekiban.EventSourcing.AggregateCommands;

public abstract class CreateAggregateCommandHandlerBase<T, C> : ICreateAggregateCommandHandler<T, C>
    where T : IAggregate where C : ICreateAggregateCommand<T>, new()
{

    public async Task<AggregateCommandResponse> HandleAsync(AggregateCommandDocument<C> command, T aggregate)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        if (command is IOnlyPublishingCommand)
        {
            throw new SekibanCanNotExecuteOnlyPublishingEventCommand(typeof(C).Name);
        }
        await ExecCreateCommandAsync(aggregate, command.Payload);
        return await Task.FromResult(new AggregateCommandResponse(aggregate.AggregateId, aggregate.Events, aggregate.Version));
    }
    public abstract Guid GenerateAggregateId(C command);
    public virtual C CleanupCommandIfNeeded(C command)
    {
        return command;
    }

    protected abstract Task ExecCreateCommandAsync(T aggregate, C command);
}
