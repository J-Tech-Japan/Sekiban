using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using System.Collections.Immutable;
using EventHandler = Sekiban.Core.Aggregate.EventHandler;
namespace Sekiban.Core.Command;

public abstract class CreateCommandHandlerBase<TAggregatePayload, TCommand> : ICreateCommandHandler<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayload, new() where TCommand : ICreateCommand<TAggregatePayload>, new()
{
    private readonly List<IEvent> _events = new();
    private Aggregate<TAggregatePayload>? _aggregate;
    public async Task<CommandResponse> HandleAsync(CommandDocument<TCommand> command, Aggregate<TAggregatePayload> aggregate)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        if (command is IOnlyPublishingCommand)
        {
            throw new SekibanCanNotExecuteOnlyPublishingEventCommand(typeof(TCommand).Name);
        }
        _aggregate = new Aggregate<TAggregatePayload> { AggregateId = aggregate.AggregateId };
        var eventPayloads = ExecCreateCommandAsync(GetAggregateState, command.Payload);
        await foreach (var eventPayload in eventPayloads)
        {
            _events.Add(EventHandler.HandleEvent(aggregate, eventPayload));
            if (_events.First().GetPayload() is not ICreatedEventPayload) { throw new SekibanCreateCommandShouldSaveCreateEventFirstException(); }
            if (_events.Count > 1 && _events.Last().GetPayload() is ICreatedEventPayload)
            {
                throw new SekibanCreateCommandShouldOnlySaveFirstException();
            }
        }
        return await Task.FromResult(new CommandResponse(aggregate.AggregateId, _events.ToImmutableList(), aggregate.Version));
    }
    public virtual TCommand CleanupCommandIfNeeded(TCommand command) => command;
    private AggregateState<TAggregatePayload> GetAggregateState()
    {
        if (_aggregate is null)
        {
            throw new SekibanCommandHandlerAggregateNullException();
        }
        var state = _aggregate.ToState();
        var aggregate = new Aggregate<TAggregatePayload>
            { AggregateId = _aggregate.AggregateId };
        aggregate.ApplySnapshot(state);
        foreach (var ev in _events)
        {
            aggregate.ApplyEvent(ev);
        }
        state = aggregate.ToState();
        return state;
    }
    protected abstract IAsyncEnumerable<IApplicableEvent<TAggregatePayload>> ExecCreateCommandAsync(
        Func<AggregateState<TAggregatePayload>> getAggregateState,
        TCommand command);
}
