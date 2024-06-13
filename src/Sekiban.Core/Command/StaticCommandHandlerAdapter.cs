using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleProjections;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

public sealed class StaticCommandHandlerAdapter<TAggregatePayload, TCommand>(
    IAggregateLoader aggregateLoader,
    IServiceProvider serviceProvider,
    bool checkVersion = true) : ICommandContext<TAggregatePayload> where TAggregatePayload : IAggregatePayloadGeneratable<TAggregatePayload>
    where TCommand : ICommandWithStaticHandlerCommon<TAggregatePayload, TCommand>
{
    private readonly List<IEvent> _events = [];
    private Aggregate<TAggregatePayload>? _aggregate;
    private string _rootPartitionKey = string.Empty;

    public AggregateState<TAggregatePayload> GetState() => GetAggregateState();
    public ResultBox<T1> GetRequiredService<T1>() where T1 : class => ResultBox.WrapTry(() => serviceProvider.GetRequiredService<T1>());

    public ResultBox<TwoValues<T1, T2>> GetRequiredService<T1, T2>() where T1 : class where T2 : class =>
        GetRequiredService<T1>().Combine(ResultBox.WrapTry(() => serviceProvider.GetRequiredService<T2>()));

    public ResultBox<ThreeValues<T1, T2, T3>> GetRequiredService<T1, T2, T3>() where T1 : class where T2 : class where T3 : class =>
        GetRequiredService<T1, T2>().Combine(ResultBox.WrapTry(() => serviceProvider.GetRequiredService<T3>()));

    public ResultBox<FourValues<T1, T2, T3, T4>> GetRequiredService<T1, T2, T3, T4>()
        where T1 : class where T2 : class where T3 : class where T4 : class =>
        GetRequiredService<T1, T2, T3>().Combine(ResultBox.WrapTry(() => serviceProvider.GetRequiredService<T4>()));

    public ResultBox<UnitValue> AppendEvent(IEventPayloadApplicableTo<TAggregatePayload> eventPayload) =>
        ResultBox.Start.Conveyor(_ => _aggregate is not null ? ResultBox.FromValue(_aggregate) : new SekibanCommandHandlerAggregateNullException())
            .Scan(aggregate => _events.Add(EventHelper.HandleEvent(aggregate, eventPayload, _rootPartitionKey)))
            .Remap(_ => UnitValue.None);

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
    public async Task<CommandResponse> HandleCommandAsync(CommandDocument<TCommand> commandDocument, Guid aggregateId, string rootPartitionKey)
    {
        var command = commandDocument.Payload;
        _aggregate = await aggregateLoader.AsAggregateAsync<TAggregatePayload>(aggregateId, rootPartitionKey) ??
            new Aggregate<TAggregatePayload> { AggregateId = aggregateId };
        _rootPartitionKey = rootPartitionKey;
        // Check if IAggregateShouldExistCommand and Aggregate does not exist
        if (command is IAggregateShouldExistCommand && _aggregate.Version == 0)
        {
            throw new SekibanAggregateNotExistsException(aggregateId, nameof(TAggregatePayload), rootPartitionKey);
        }

        // Validate AddAggregate Version
        if (checkVersion && command is IVersionValidationCommandCommon validationCommand && validationCommand.ReferenceVersion != _aggregate.Version)
        {
            throw new SekibanCommandInconsistentVersionException(
                _aggregate.AggregateId,
                validationCommand.ReferenceVersion,
                _aggregate.Version,
                rootPartitionKey);
        }
        var state = _aggregate.ToState();

        // Validate AddAggregate is deleted
        if (state.GetIsDeleted() && command is not ICancelDeletedCommand)
        {
            throw new SekibanAggregateAlreadyDeletedException();
        }

        switch (command)
        {
            case ICommandWithStaticHandler<TAggregatePayload, TCommand> regularHandler:
            {
                // execute static HandleCommand of typeof(command) using reflection
                var commandType = command.GetType();
                var method = commandType.GetMethod("HandleCommand");
                if (method is null)
                {
                    throw new SekibanCommandHandlerNotMatchException(
                        commandType.Name + "handler should inherit " + typeof(ICommandWithStaticHandler<,>).Name);
                }
                var result = method.Invoke(null, new object[] { command, this }) as ResultBox<UnitValue>;
                if (result is null)
                {
                    throw new SekibanCommandHandlerNotMatchException(
                        commandType.Name + "handler should inherit " + typeof(ICommandWithStaticHandler<,>).Name);
                }
                result.UnwrapBox();

                return new CommandResponse(
                    _aggregate.AggregateId,
                    _events.ToImmutableList(),
                    _aggregate.Version,
                    _events.Max(m => m.SortableUniqueId));
            }
            case ICommandWithStaticHandlerAsync<TAggregatePayload, TCommand> asyncHandler:
            {
                // execute static HandleCommandAsync of typeof(command) using reflection
                var commandType = command.GetType();
                var method = commandType.GetMethod("HandleCommandAsync");
                if (method is null)
                {
                    throw new SekibanCommandHandlerNotMatchException(
                        commandType.Name + "handler should inherit " + typeof(ICommandWithStaticHandlerAsync<,>).Name);
                }
                var resultAsync = method.Invoke(null, new object[] { command, this }) as Task<ResultBox<UnitValue>>;
                if (resultAsync is null)
                {
                    throw new SekibanCommandHandlerNotMatchException(
                        commandType.Name + "handler should inherit " + typeof(ICommandWithStaticHandlerAsync<,>).Name);
                }
                var result = await resultAsync;
                result.UnwrapBox();

                return new CommandResponse(
                    _aggregate.AggregateId,
                    _events.ToImmutableList(),
                    _aggregate.Version,
                    _events.Max(m => m.SortableUniqueId));
            }
            default:
                throw new SekibanCommandHandlerNotMatchException(
                    command.GetType().Name + "handler should inherit " + typeof(ICommandHandler<,>).Name);
        }

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