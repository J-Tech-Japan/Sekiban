using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

public abstract class CreateAggregateCommandHandlerBase<TAggregatePayload, TCommand> : ICreateAggregateCommandHandler<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayload, new() where TCommand : ICreateAggregateCommand<TAggregatePayload>, new()
{
    private readonly List<IAggregateEvent> _events = new();
    private Aggregate<TAggregatePayload>? _aggregate;
    public async Task<AggregateCommandResponse> HandleAsync(AggregateCommandDocument<TCommand> command, Aggregate<TAggregatePayload> aggregate)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        if (command is IOnlyPublishingCommand)
        {
            throw new SekibanCanNotExecuteOnlyPublishingEventCommand(typeof(TCommand).Name);
        }
        _aggregate = aggregate;
        var eventPayloads = ExecCreateCommandAsync(GetAggregateState, command.Payload);
        await foreach (var eventPayload in eventPayloads)
        {
            _events.Add(AggregateEventHandler.HandleAggregateEvent(aggregate, eventPayload));
            if (_events.First().GetPayload() is not ICreatedEventPayload) { throw new SekibanCreateCommandShouldSaveCreateEventFirstException(); }
            if (_events.Count > 1 && _events.Last().GetPayload() is ICreatedEventPayload) { throw new SekibanCreateCommandShouldOnlySaveFirstException(); }
        }
        return await Task.FromResult(new AggregateCommandResponse(aggregate.AggregateId, _events.ToImmutableList(), aggregate.Version));
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
    protected abstract IAsyncEnumerable<IApplicableEvent<TAggregatePayload>> ExecCreateCommandAsync(
        Func<AggregateState<TAggregatePayload>> getAggregateState,
        TCommand command);
}
