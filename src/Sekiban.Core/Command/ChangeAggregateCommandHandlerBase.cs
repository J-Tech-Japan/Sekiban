using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

public abstract class ChangeAggregateCommandHandlerBase<TAggregatePayload, TCommand> : IChangeAggregateCommandHandler<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayload, new() where TCommand : ChangeAggregateCommandBase<TAggregatePayload>, new()
{
    private readonly List<IAggregateEvent> _events = new();
    private Aggregate<TAggregatePayload>? _aggregate;
    public async Task<AggregateCommandResponse> HandleAsync(
        AggregateCommandDocument<TCommand> aggregateCommandDocument,
        Aggregate<TAggregatePayload> aggregate)
    {
        var command = aggregateCommandDocument.Payload;
        _aggregate = aggregate;
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
        var eventPayloads = ExecCommandAsync(GetAggregateState, command);
        await foreach (var eventPayload in eventPayloads)
        {
            _events.Add(AggregateEventHandler.HandleAggregateEvent(aggregate, eventPayload));
        }
        return await Task.FromResult(new AggregateCommandResponse(aggregate.AggregateId, _events.ToImmutableList(), aggregate.Version));
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

    private AggregateState<TAggregatePayload> GetAggregateState()
    {
        if (_aggregate is null)
        {
            throw new SekibanCommandHandlerAggregateNullException();
        }
        var state = _aggregate.ToState();
        foreach (var ev in _events)
        {
            var aggregate = new Aggregate<TAggregatePayload>();
            aggregate.ApplySnapshot(state);
            aggregate.ApplyEvent(ev);
            state = aggregate.ToState();
        }
        return state;
    }

    protected abstract IAsyncEnumerable<IChangedEvent<TAggregatePayload>> ExecCommandAsync(
        Func<AggregateState<TAggregatePayload>> getAggregateState,
        TCommand command);
}
