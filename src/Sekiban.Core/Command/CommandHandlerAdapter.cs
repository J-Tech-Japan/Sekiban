using System.Collections.Immutable;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleProjections;

namespace Sekiban.Core.Command;

public class CommandHandlerAdapter<TAggregatePayload, TCommand> where TAggregatePayload : IAggregatePayload, new()
    where TCommand : ICommand<TAggregatePayload>
{
    private readonly IAggregateLoader _aggregateLoader;
    private readonly bool _checkVersion;
    private readonly List<IEvent> _events = new();
    private Aggregate<TAggregatePayload>? _aggregate;

    public CommandHandlerAdapter(IAggregateLoader aggregateLoader, bool checkVersion = true)
    {
        _aggregateLoader = aggregateLoader;
        _checkVersion = checkVersion;
    }

    public async Task<CommandResponse> HandleCommandAsync(
        CommandDocument<TCommand> commandDocument,
        ICommandHandlerCommon<TAggregatePayload, TCommand> handler,
        Guid aggregateId)
    {
        var command = commandDocument.Payload;
        _aggregate = await _aggregateLoader.AsAggregateAsync<TAggregatePayload>(aggregateId) ??
                     new Aggregate<TAggregatePayload>
                         { AggregateId = aggregateId };
        if (handler is not ICommandHandlerBase<TAggregatePayload, TCommand> regularHandler)
            throw new SekibanCommandHandlerNotMatchException(
                handler.GetType().Name + "handler should inherit " + typeof(ICommandHandlerBase<,>).Name);
        var state = _aggregate.ToState();
        // Validate AddAggregate is deleted
        if (state.GetIsDeleted() && command is not ICancelDeletedCommand)
            throw new SekibanAggregateAlreadyDeletedException();

        // Validate AddAggregate Version
        if (_checkVersion && command is IVersionValidationCommandCommon validationCommand &&
            validationCommand.ReferenceVersion != _aggregate.Version)
            throw new SekibanCommandInconsistentVersionException(
                _aggregate.AggregateId,
                validationCommand.ReferenceVersion,
                _aggregate.Version);
        await foreach (var eventPayload in regularHandler.HandleCommandAsync(GetAggregateState, command))
            _events.Add(EventHelper.HandleEvent(_aggregate, eventPayload));
        return new CommandResponse(_aggregate.AggregateId, _events.ToImmutableList(), _aggregate.Version);
    }

    private AggregateState<TAggregatePayload> GetAggregateState()
    {
        if (_aggregate is null) throw new SekibanCommandHandlerAggregateNullException();
        var state = _aggregate.ToState();
        var aggregate = new Aggregate<TAggregatePayload>();
        aggregate.ApplySnapshot(state);
        foreach (var ev in _events) aggregate.ApplyEvent(ev);
        state = aggregate.ToState();
        return state;
    }
}
