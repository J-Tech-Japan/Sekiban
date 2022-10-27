using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

public abstract class ChangeAggregateCommandHandlerBase<TAggregatePayload, TCommand> : IChangeAggregateCommandHandler<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayload, new() where TCommand : ChangeAggregateCommandBase<TAggregatePayload>, new()
{
    public async Task<AggregateCommandResponse> HandleAsync(
        AggregateCommandDocument<TCommand> aggregateCommandDocument,
        Aggregate<TAggregatePayload> aggregate)
    {
        var command = aggregateCommandDocument.Payload;

        if (command is IOnlyPublishingCommand)
        {
            throw new SekibanCanNotExecuteOnlyPublishingEventCommand(typeof(TCommand).Name);
        }
        var state = aggregate.ToState();
        // Validate Aggregate is deleted
        if (state.GetIsDeleted() && command is not ICancelDeletedCommand)
        {
            throw new SekibanAggregateAlreadyDeletedException();
        }

        // Validate Aggregate Version
        if (command is not INoValidateCommand && command.ReferenceVersion != aggregate.Version)
        {
            throw new SekibanAggregateCommandInconsistentVersionException(aggregate.AggregateId, command.ReferenceVersion, aggregate.Version);
        }

        // Execute Command
        var eventPayloads = ExecCommandAsync(aggregate.ToState(), command);
        var events = new List<IAggregateEvent>();
        await foreach (var eventPayload in eventPayloads)
        {
            events.Add(AggregateEventHandler.HandleAggregateEvent(aggregate, eventPayload));
        }
        return await Task.FromResult(new AggregateCommandResponse(aggregate.AggregateId, events.ToImmutableList(), aggregate.Version));
    }
    public Task<AggregateCommandResponse> HandleForOnlyPublishingCommandAsync(
        AggregateCommandDocument<TCommand> aggregateCommandDocument,
        Guid aggregateId)
    {
        throw new SekibanCanNotExecuteOnlyPublishingEventCommand(typeof(TCommand).Name);
    }
    public virtual TCommand CleanupCommandIfNeeded(TCommand command)
    {
        return command;
    }

    protected abstract IAsyncEnumerable<IChangedEvent<TAggregatePayload>> ExecCommandAsync(
        AggregateState<TAggregatePayload> aggregateState,
        TCommand command);
}
