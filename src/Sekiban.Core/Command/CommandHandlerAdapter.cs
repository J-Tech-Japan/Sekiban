using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleProjections;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

/// <summary>
///     System use command handler adapter.
///     Application Developer does not need to implement this interface
/// </summary>
public sealed class CommandHandlerAdapter<TAggregatePayload, TCommand> where TAggregatePayload : IAggregatePayloadCommonBase
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
    /// <summary>
    ///     Common Command handler, it is used for test and production code.
    ///     internal use only
    /// </summary>
    /// <param name="commandDocument"></param>
    /// <param name="handler"></param>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <returns></returns>
    /// <exception cref="SekibanCommandHandlerNotMatchException">When command is called with wrong context.</exception>
    /// <exception cref="SekibanAggregateAlreadyDeletedException">When Command is called for deleted aggregate</exception>
    /// <exception cref="SekibanCommandInconsistentVersionException">When optimistic version check failed.</exception>
    public async Task<CommandResponse> HandleCommandAsync(
        CommandDocument<TCommand> commandDocument,
        ICommandHandlerCommon<TAggregatePayload, TCommand> handler,
        Guid aggregateId,
        string rootPartitionKey)
    {
        var command = commandDocument.Payload;
        _aggregate = await _aggregateLoader.AsAggregateAsync<TAggregatePayload>(aggregateId, rootPartitionKey) ??
            new Aggregate<TAggregatePayload> { AggregateId = aggregateId };
        if (handler is not ICommandHandler<TAggregatePayload, TCommand> regularHandler)
        {
            throw new SekibanCommandHandlerNotMatchException(handler.GetType().Name + "handler should inherit " + typeof(ICommandHandler<,>).Name);
        }
        var state = _aggregate.ToState();
        // Validate AddAggregate is deleted
        if (state.GetIsDeleted() && command is not ICancelDeletedCommand)
        {
            throw new SekibanAggregateAlreadyDeletedException();
        }

        // Validate AddAggregate Version
        if (_checkVersion && command is IVersionValidationCommandCommon validationCommand && validationCommand.ReferenceVersion != _aggregate.Version)
        {
            throw new SekibanCommandInconsistentVersionException(
                _aggregate.AggregateId,
                validationCommand.ReferenceVersion,
                _aggregate.Version,
                rootPartitionKey);
        }
        await foreach (var eventPayload in regularHandler.HandleCommandAsync(GetAggregateState, command))
        {
            _events.Add(EventHelper.HandleEvent(_aggregate, eventPayload, rootPartitionKey));
        }
        return new CommandResponse(_aggregate.AggregateId, _events.ToImmutableList(), _aggregate.Version, _events.Max(m => m.SortableUniqueId));
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
