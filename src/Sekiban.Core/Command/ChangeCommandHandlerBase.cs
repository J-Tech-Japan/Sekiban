﻿using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using System.Collections.Immutable;
using EventHandler = Sekiban.Core.Aggregate.EventHandler;
namespace Sekiban.Core.Command;

public abstract class ChangeCommandHandlerBase<TAggregatePayload, TCommand> : IChangeCommandHandler<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayload, new() where TCommand : ChangeCommandBase<TAggregatePayload>, new()
{
    private readonly List<IEvent> _events = new();
    private AggregateIdentifier<TAggregatePayload>? _aggregate;
    public async Task<CommandResponse> HandleAsync(
        CommandDocument<TCommand> commandDocument,
        AggregateIdentifier<TAggregatePayload> aggregateIdentifier)
    {
        var command = commandDocument.Payload;
        _aggregate = aggregateIdentifier;
        if (command is IOnlyPublishingCommand)
        {
            throw new SekibanCanNotExecuteOnlyPublishingEventCommand(typeof(TCommand).Name);
        }
        var state = aggregateIdentifier.ToState();
        // Validate AddAggregate is deleted
        if (state.GetIsDeleted() && command is not ICancelDeletedCommand)
        {
            throw new SekibanAggregateAlreadyDeletedException();
        }

        // Validate AddAggregate Version
        if (command is not INoValidateCommand && command.ReferenceVersion != aggregateIdentifier.Version)
        {
            throw new SekibanCommandInconsistentVersionException(
                aggregateIdentifier.AggregateId,
                command.ReferenceVersion,
                aggregateIdentifier.Version);
        }

        // Execute Command
        var eventPayloads = ExecCommandAsync(GetAggregateState, command);
        await foreach (var eventPayload in eventPayloads)
        {
            _events.Add(EventHandler.HandleEvent(aggregateIdentifier, eventPayload));
        }
        return await Task.FromResult(new CommandResponse(aggregateIdentifier.AggregateId, _events.ToImmutableList(), aggregateIdentifier.Version));
    }
    public Task<CommandResponse> HandleForOnlyPublishingCommandAsync(
        CommandDocument<TCommand> commandDocument,
        Guid aggregateId) => throw new SekibanCanNotExecuteOnlyPublishingEventCommand(typeof(TCommand).Name);
    public virtual TCommand CleanupCommandIfNeeded(TCommand command) => command;

    private AggregateIdentifierState<TAggregatePayload> GetAggregateState()
    {
        if (_aggregate is null)
        {
            throw new SekibanCommandHandlerAggregateNullException();
        }
        var state = _aggregate.ToState();
        foreach (var ev in _events)
        {
            var aggregate = new AggregateIdentifier<TAggregatePayload>();
            aggregate.ApplySnapshot(state);
            aggregate.ApplyEvent(ev);
            state = aggregate.ToState();
        }
        return state;
    }

    protected abstract IAsyncEnumerable<IChangedEvent<TAggregatePayload>> ExecCommandAsync(
        Func<AggregateIdentifierState<TAggregatePayload>> getAggregateState,
        TCommand command);
}