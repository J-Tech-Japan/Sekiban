using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

public sealed class StaticCommandHandlerAdapter<TAggregatePayloadBase, TAggregatePayloadState, TCommand>(
    IAggregateLoader aggregateLoader,
    IServiceProvider serviceProvider,
    bool checkVersion = true)
    : ICommandContext<TAggregatePayloadState> where TAggregatePayloadBase : IAggregatePayloadCommon
    where TAggregatePayloadState : IAggregatePayloadCommon
    where TCommand : ICommandWithHandlerCommon<TAggregatePayloadState, TCommand>
{
    private readonly List<IEvent> _events = [];
    private Aggregate<TAggregatePayloadBase>? _aggregate;
    private string _rootPartitionKey = string.Empty;

    public AggregateState<TAggregatePayloadState> GetState() => GetAggregateState();
    public ResultBox<T1> GetRequiredService<T1>() where T1 : class =>
        ResultBox.WrapTry(() => serviceProvider.GetRequiredService<T1>());

    public ResultBox<TwoValues<T1, T2>> GetRequiredService<T1, T2>() where T1 : class where T2 : class =>
        GetRequiredService<T1>().Combine(ResultBox.WrapTry(() => serviceProvider.GetRequiredService<T2>()));

    public ResultBox<ThreeValues<T1, T2, T3>> GetRequiredService<T1, T2, T3>()
        where T1 : class where T2 : class where T3 : class =>
        GetRequiredService<T1, T2>().Combine(ResultBox.WrapTry(() => serviceProvider.GetRequiredService<T3>()));

    public ResultBox<FourValues<T1, T2, T3, T4>> GetRequiredService<T1, T2, T3, T4>()
        where T1 : class where T2 : class where T3 : class where T4 : class =>
        GetRequiredService<T1, T2, T3>().Combine(ResultBox.WrapTry(() => serviceProvider.GetRequiredService<T4>()));

    public ResultBox<UnitValue> AppendEvent(IEventPayloadApplicableTo<TAggregatePayloadState> eventPayload) =>
        ResultBox
            .Start
            .Conveyor(
                _ => _aggregate is not null
                    ? ResultBox.FromValue(_aggregate)
                    : new SekibanCommandHandlerAggregateNullException())
            .Scan(aggregate => _events.Add(EventHelper.HandleEvent(aggregate, eventPayload, _rootPartitionKey)))
            .Remap(_ => UnitValue.None);
    public ResultBox<UnitValue> AppendEvents(
        params IEventPayloadApplicableTo<TAggregatePayloadState>[] eventPayloads) =>
        ResultBox
            .FromValue(eventPayloads.ToList())
            .ReduceEach(UnitValue.Unit, (nextEventPayload, _) => AppendEvent(nextEventPayload));
    public Task<ResultBox<AggregateState<TAnotherAggregatePayload>>> GetAggregateState<TAnotherAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        SingleProjectionRetrievalOptions? retrievalOptions = null)
        where TAnotherAggregatePayload : IAggregatePayloadCommon =>
        GetRequiredService<IAggregateLoader>()
            .Conveyor(
                executor => executor.AsDefaultStateWithResultAsync<TAnotherAggregatePayload>(
                    aggregateId,
                    rootPartitionKey,
                    toVersion));
    public Task<ResultBox<AggregateState<TAnotherAggregatePayload>>>
        GetAggregateStateFromInitial<TAnotherAggregatePayload>(
            Guid aggregateId,
            string rootPartitionKey = IDocument.DefaultRootPartitionKey,
            int? toVersion = null) where TAnotherAggregatePayload : IAggregatePayloadCommon =>
        GetRequiredService<IAggregateLoader>()
            .Conveyor(
                loader => loader.AsDefaultStateFromInitialWithResultAsync<TAnotherAggregatePayload>(
                    aggregateId,
                    rootPartitionKey,
                    toVersion));
    public Task<ResultBox<SingleProjectionState<TSingleProjectionPayload>>>
        GetSingleProjectionStateFromInitialAsync<TSingleProjectionPayload>(
            Guid aggregateId,
            string rootPartitionKey = IDocument.DefaultRootPartitionKey,
            int? toVersion = null) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        GetRequiredService<IAggregateLoader>()
            .Conveyor(
                loader => ResultBox.CheckNullWrapTry(
                    () => loader.AsSingleProjectionStateFromInitialAsync<TSingleProjectionPayload>(
                        aggregateId,
                        rootPartitionKey,
                        toVersion)));
    public Task<ResultBox<SingleProjectionState<TSingleProjectionPayload>>>
        GetSingleProjectionStateAsync<TSingleProjectionPayload>(
            Guid aggregateId,
            string rootPartitionKey = IDocument.DefaultRootPartitionKey,
            int? toVersion = null,
            SingleProjectionRetrievalOptions? retrievalOptions = null)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        GetRequiredService<IAggregateLoader>()
            .Conveyor(
                loader => ResultBox.CheckNullWrapTry(
                    () => loader.AsSingleProjectionStateAsync<TSingleProjectionPayload>(
                        aggregateId,
                        rootPartitionKey,
                        toVersion)));
    public Task<ResultBox<TOutput>> ExecuteQueryAsync<TOutput>(INextQueryCommon<TOutput> query)
        where TOutput : notnull =>
        GetRequiredService<IQueryExecutor>().Conveyor(executor => executor.ExecuteAsync(query));
    public Task<ResultBox<ListQueryResult<TOutput>>> ExecuteQueryAsync<TOutput>(INextListQueryCommon<TOutput> query)
        where TOutput : notnull =>
        GetRequiredService<IQueryExecutor>().Conveyor(executor => executor.ExecuteAsync(query));
    public Task<ResultBox<ListQueryResult<TOutput>>> ExecuteQueryAsync<TOutput>(IListQueryInput<TOutput> param)
        where TOutput : IQueryResponse =>
        GetRequiredService<IQueryExecutor>().Conveyor(executor => executor.ExecuteWithResultAsync(param));
    public Task<ResultBox<TOutput>> ExecuteQueryAsync<TOutput>(IQueryInput<TOutput> param)
        where TOutput : IQueryResponse =>
        GetRequiredService<IQueryExecutor>().Conveyor(executor => executor.ExecuteWithResultAsync(param));

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
    public async Task<ResultBox<CommandResponse>> HandleCommandAsync(
        CommandDocument<TCommand> commandDocument,
        Guid aggregateId,
        string rootPartitionKey)
    {
        var command = commandDocument.Payload;
        _aggregate = await aggregateLoader.AsAggregateAsync<TAggregatePayloadBase>(aggregateId, rootPartitionKey) ??
            new Aggregate<TAggregatePayloadBase> { AggregateId = aggregateId };
        _rootPartitionKey = rootPartitionKey;
        // Check if IAggregateShouldExistCommand and Aggregate does not exist
        if (command is IAggregateShouldExistCommand && _aggregate.Version == 0)
        {
            return ResultBox<CommandResponse>.FromException(
                new SekibanAggregateNotExistsException(aggregateId, nameof(TAggregatePayloadBase), rootPartitionKey));
        }

        // Validate AddAggregate Version
        if (checkVersion &&
            command is IVersionValidationCommandCommon validationCommand &&
            validationCommand.ReferenceVersion != _aggregate.Version)
        {
            return ResultBox<CommandResponse>.FromException(
                new SekibanCommandInconsistentVersionException(
                    _aggregate.AggregateId,
                    validationCommand.ReferenceVersion,
                    _aggregate.Version,
                    rootPartitionKey));
        }
        var state = _aggregate.ToState<TAggregatePayloadState>();

        // Validate AddAggregate is deleted
        if (state.GetIsDeleted() && command is not ICancelDeletedCommand)
        {
            return ResultBox<CommandResponse>.FromException(new SekibanAggregateAlreadyDeletedException());
        }

        switch (command)
        {
            case not null when command is ICommandWithHandler<TAggregatePayloadState, TCommand>:
            {
                // execute static HandleCommand of typeof(command) using reflection
                var commandType = command.GetType();
                var method = commandType.GetMethod("HandleCommand");
                if (method is null)
                {
                    return ResultBox<CommandResponse>.FromException(
                        new SekibanCommandHandlerNotMatchException(
                            commandType.Name + "handler should inherit " + typeof(ICommandWithHandler<,>).Name));
                }
                var result = method.Invoke(null, new object[] { command, this }) as ResultBox<UnitValue>;
                if (result is null)
                {
                    return ResultBox<CommandResponse>.FromException(
                        new SekibanCommandHandlerNotMatchException(
                            commandType.Name + "handler should inherit " + typeof(ICommandWithHandler<,>).Name));
                }
                result.UnwrapBox();

                return result.Remap(
                    _ => new CommandResponse(
                        _aggregate.AggregateId,
                        _events.ToImmutableList(),
                        _aggregate.Version,
                        _events.Max(m => m.SortableUniqueId)));
            }
            case not null when command is ICommandWithHandlerAsync<TAggregatePayloadState, TCommand>:
            {
                // execute static HandleCommandAsync of typeof(command) using reflection
                var commandType = command.GetType();
                var method = commandType.GetMethod("HandleCommandAsync");
                if (method is null)
                {
                    return ResultBox<CommandResponse>.FromException(
                        new SekibanCommandHandlerNotMatchException(
                            commandType.Name + "handler should inherit " + typeof(ICommandWithHandlerAsync<,>).Name));
                }
                var resultAsync = method.Invoke(null, new object[] { command, this }) as Task<ResultBox<UnitValue>>;
                if (resultAsync is null)
                {
                    return ResultBox<CommandResponse>.FromException(
                        new SekibanCommandHandlerNotMatchException(
                            commandType.Name + "handler should inherit " + typeof(ICommandWithHandlerAsync<,>).Name));
                }
                var result = await resultAsync;

                return result.Remap(
                    _ => new CommandResponse(
                        _aggregate.AggregateId,
                        _events.ToImmutableList(),
                        _aggregate.Version,
                        _events.Max(m => m.SortableUniqueId)));
            }
            default:
                return ResultBox<CommandResponse>.FromException(
                    new SekibanCommandHandlerNotMatchException(
                        command?.GetType().Name + "handler should inherit " + typeof(ICommandHandler<,>).Name));
        }

    }

    private AggregateState<TAggregatePayloadState> GetAggregateState()
    {
        if (_aggregate is null)
        {
            throw new SekibanCommandHandlerAggregateNullException();
        }
        var state = _aggregate.ToState<TAggregatePayloadState>();
        var aggregate = new Aggregate<TAggregatePayloadBase>();
        aggregate.ApplySnapshot(state);
        foreach (var ev in _events)
        {
            aggregate.ApplyEvent(ev);
        }
        state = aggregate.ToState<TAggregatePayloadState>();
        return state;
    }
}
