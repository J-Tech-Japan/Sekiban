namespace Sekiban.EventSourcing.AggregateCommands;

public abstract class ChangeAggregateCommandHandlerBase<T, C> : IChangeAggregateCommandHandler<T, C>
    where T : IAggregate where C : ChangeAggregateCommandBase<T>
{
    async Task<AggregateCommandResponse<T>> IChangeAggregateCommandHandler<T, C>.HandleAsync(
        AggregateCommandDocument<C> aggregateCommandDocument,
        T aggregate)
    {
        var command = aggregateCommandDocument.Payload;

        // Validate Aggregate is deleted
        if (command is not INoValidateCommand && aggregate.IsDeleted)
        {
            throw new JJAggregateNotExistsException(aggregate.AggregateId, typeof(T).Name);
        }

        // Validate Aggregate Version
        if (command is not INoValidateCommand && command.ReferenceVersion != aggregate.Version)
        {
            throw new JJAggregateCommandInconsistentVersionException(aggregate.AggregateId, aggregate.Version);
        }

        // Execute Command
        await ExecCommandAsync(aggregate, command);
        return await Task.FromResult(new AggregateCommandResponse<T>(aggregate));
    }

    protected abstract Task ExecCommandAsync(T aggregate, C command);
}
