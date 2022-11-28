using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleProjections;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

public class CommandHandlerAdapter<TAggregatePayload, TCommand> where TAggregatePayload : IAggregatePayload, new()
    where TCommand : ICommandBase<TAggregatePayload>
{
    private readonly Aggregate<TAggregatePayload>? _aggregate = null;
    private readonly IAggregateLoader _aggregateLoader;
    private readonly bool _checkVersion;
    private readonly List<IEvent> _events = new();
    public CommandHandlerAdapter(IAggregateLoader aggregateLoader, bool checkVersion = true)
    {
        _aggregateLoader = aggregateLoader;
        _checkVersion = checkVersion;
    }

    public async Task<CommandResponse> HandleRegularCommandAsync(
        CommandDocument<TCommand> commandDocument,
        ICommandHandler<TAggregatePayload, TCommand> handler,
        Guid aggregateId)
    {
        var command = commandDocument.Payload;
        var aggregate = await _aggregateLoader.AsAggregateAsync<TAggregatePayload>(aggregateId) ??
            new Aggregate<TAggregatePayload>
                { AggregateId = aggregateId };
        if (handler is not ICommandHandlerBase<TAggregatePayload, TCommand> regularHandler)
        {
            throw new SekibanCommandHandlerNotMatchException(
                handler.GetType().Name + "handler should inherit " + typeof(ICommandHandlerBase<,>).Name);
        }
        var state = aggregate.ToState();
        // Validate AddAggregate is deleted
        if (state.GetIsDeleted() && command is not ICancelDeletedCommand)
        {
            throw new SekibanAggregateAlreadyDeletedException();
        }

        // Validate AddAggregate Version
        if (_checkVersion && command is IVersionValidationCommand validationCommand && validationCommand.ReferenceVersion != aggregate.Version)
        {
            throw new SekibanCommandInconsistentVersionException(
                aggregate.AggregateId,
                validationCommand.ReferenceVersion,
                aggregate.Version);
        }
        await foreach (var eventPayload in regularHandler.HandleCommandAsync(GetAggregateState, command))
        {
            _events.Add(EventHelper.HandleEvent(aggregate, eventPayload));
        }
        return new CommandResponse(aggregate.AggregateId, _events.ToImmutableList(), aggregate.Version);
    }

    private AggregateState<TAggregatePayload> GetAggregateState()
    {
        if (_aggregate is null)
        {
            throw new SekibanCommandHandlerAggregateNullException();
        }
        var state = _aggregate.ToState();
        var aggregate = new Aggregate<TAggregatePayload>();
        aggregate.ApplySnapshot(state);
        foreach (var ev in _events)
        {
            aggregate.ApplyEvent(ev);
        }
        state = aggregate.ToState();
        return state;
    }
}
