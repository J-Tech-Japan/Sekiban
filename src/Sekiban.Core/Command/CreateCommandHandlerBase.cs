﻿using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using System.Collections.Immutable;
using EventHandler = Sekiban.Core.Aggregate.EventHandler;
namespace Sekiban.Core.Command;

public abstract class CreateCommandHandlerBase<TAggregatePayload, TCommand> : ICreateCommandHandler<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayload, new() where TCommand : ICreateCommand<TAggregatePayload>, new()
{
    private readonly List<IEvent> _events = new();
    private AggregateIdentifier<TAggregatePayload>? _aggregate;
    public async Task<CommandResponse> HandleAsync(CommandDocument<TCommand> command, AggregateIdentifier<TAggregatePayload> aggregateIdentifier)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        if (command is IOnlyPublishingCommand)
        {
            throw new SekibanCanNotExecuteOnlyPublishingEventCommand(typeof(TCommand).Name);
        }
        _aggregate = aggregateIdentifier;
        var eventPayloads = ExecCreateCommandAsync(GetAggregateState, command.Payload);
        await foreach (var eventPayload in eventPayloads)
        {
            _events.Add(EventHandler.HandleEvent(aggregateIdentifier, eventPayload));
            if (_events.First().GetPayload() is not ICreatedEventPayload) { throw new SekibanCreateCommandShouldSaveCreateEventFirstException(); }
            if (_events.Count > 1 && _events.Last().GetPayload() is ICreatedEventPayload)
            {
                throw new SekibanCreateCommandShouldOnlySaveFirstException();
            }
        }
        return await Task.FromResult(new CommandResponse(aggregateIdentifier.AggregateId, _events.ToImmutableList(), aggregateIdentifier.Version));
    }
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
    protected abstract IAsyncEnumerable<IApplicableEvent<TAggregatePayload>> ExecCreateCommandAsync(
        Func<AggregateIdentifierState<TAggregatePayload>> getAggregateState,
        TCommand command);
}