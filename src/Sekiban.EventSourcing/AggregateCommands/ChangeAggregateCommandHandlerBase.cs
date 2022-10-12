namespace Sekiban.EventSourcing.AggregateCommands;

public abstract class ChangeAggregateCommandHandlerBase<T, C> : IChangeAggregateCommandHandler<T, C>
    where T : IAggregate where C : ChangeAggregateCommandBase<T>, new()
{
    public async Task<AggregateCommandResponse> HandleAsync(AggregateCommandDocument<C> aggregateCommandDocument, T aggregate)
    {
        var command = aggregateCommandDocument.Payload;

        if (command is IOnlyPublishingCommand)
        {
            throw new SekibanCanNotExecuteOnlyPublishingEventCommand(typeof(C).Name);
        }

        // Validate Aggregate is deleted
        if (command is not INoValidateCommand && aggregate.IsDeleted)
        {
            throw new SekibanAggregateNotExistsException(aggregate.AggregateId, typeof(T).Name);
        }

        // Validate Aggregate Version
        if (command is not INoValidateCommand && command.ReferenceVersion != aggregate.Version)
        {
            throw new SekibanAggregateCommandInconsistentVersionException(aggregate.AggregateId, aggregate.Version);
        }

        // Execute Command
        await ExecCommandAsync(aggregate, command);
        return await Task.FromResult(new AggregateCommandResponse(aggregate.AggregateId, aggregate.Events, aggregate.Version));
    }
    public Task<AggregateCommandResponse> HandleForOnlyPublishingCommandAsync(AggregateCommandDocument<C> aggregateCommandDocument, Guid aggregateId)
    {
        throw new SekibanCanNotExecuteOnlyPublishingEventCommand(typeof(C).Name);
    }
    public virtual C CleanupCommandIfNeeded(C command)
    {
        return command;
    }

    protected abstract Task ExecCommandAsync(T aggregate, C command);
}
