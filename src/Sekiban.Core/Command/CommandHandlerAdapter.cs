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

/// <summary>
///     System use command handler adapter.
///     Application Developer does not need to implement this interface
/// </summary>
public sealed class CommandHandlerAdapter<TAggregatePayload, TCommand> : ICommandContext<TAggregatePayload>
    where TAggregatePayload : IAggregatePayloadGeneratable<TAggregatePayload>
    where TCommand : ICommand<TAggregatePayload>
{
    private readonly IAggregateLoader _aggregateLoader;
    private readonly bool _checkVersion;
    private readonly List<IEvent> _events = [];
    private readonly IServiceProvider _serviceProvider;
    private Aggregate<TAggregatePayload>? _aggregate;
    private string _rootPartitionKey = string.Empty;

    public CommandHandlerAdapter(
        IAggregateLoader aggregateLoader,
        IServiceProvider serviceProvider,
        bool checkVersion = true)
    {
        _aggregateLoader = aggregateLoader;
        _serviceProvider = serviceProvider;
        _checkVersion = checkVersion;
    }
    public AggregateState<TAggregatePayload> GetState() => GetAggregateState();
    public ResultBox<T1> GetRequiredService<T1>() where T1 : class =>
        ResultBox.WrapTry(() => _serviceProvider.GetRequiredService<T1>());

    public ResultBox<TwoValues<T1, T2>> GetRequiredService<T1, T2>() where T1 : class where T2 : class =>
        GetRequiredService<T1>().Combine(ResultBox.WrapTry(() => _serviceProvider.GetRequiredService<T2>()));

    public ResultBox<ThreeValues<T1, T2, T3>> GetRequiredService<T1, T2, T3>()
        where T1 : class where T2 : class where T3 : class =>
        GetRequiredService<T1, T2>().Combine(ResultBox.WrapTry(() => _serviceProvider.GetRequiredService<T3>()));

    public ResultBox<FourValues<T1, T2, T3, T4>> GetRequiredService<T1, T2, T3, T4>()
        where T1 : class where T2 : class where T3 : class where T4 : class =>
        GetRequiredService<T1, T2, T3>().Combine(ResultBox.WrapTry(() => _serviceProvider.GetRequiredService<T4>()));
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

    public ResultBox<UnitValue> AppendEvent(IEventPayloadApplicableTo<TAggregatePayload> eventPayload) =>
        ResultBox
            .Start
            .Conveyor(
                _ => _aggregate is not null
                    ? ResultBox.FromValue(_aggregate)
                    : new SekibanCommandHandlerAggregateNullException())
            .Scan(aggregate => _events.Add(EventHelper.HandleEvent(aggregate, eventPayload, _rootPartitionKey)))
            .Remap(_ => UnitValue.None);
    public ResultBox<UnitValue> AppendEvents(params IEventPayloadApplicableTo<TAggregatePayload>[] eventPayloads) =>
        ResultBox
            .FromValue(eventPayloads.ToList())
            .ReduceEach(UnitValue.Unit, (nextEventPayload, _) => AppendEvent(nextEventPayload));

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
        _rootPartitionKey = rootPartitionKey;
        // Check if IAggregateShouldExistCommand and Aggregate does not exist
        if (command is IAggregateShouldExistCommand && _aggregate.Version == 0)
        {
            throw new SekibanAggregateNotExistsException(aggregateId, nameof(TAggregatePayload), rootPartitionKey);
        }

        // Validate AddAggregate Version
        if (_checkVersion &&
            command is IVersionValidationCommandCommon validationCommand &&
            validationCommand.ReferenceVersion != _aggregate.Version)
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

        switch (handler)
        {
            case ICommandHandler<TAggregatePayload, TCommand> regularHandler:
            {
                foreach (var eventPayload in regularHandler.HandleCommand(command, this))
                {
                    _events.Add(EventHelper.HandleEvent(_aggregate, eventPayload, rootPartitionKey));
                }
                return new CommandResponse(
                    _aggregate.AggregateId,
                    _events.ToImmutableList(),
                    _aggregate.Version,
                    _events.Max(m => m.SortableUniqueId));
            }
            case ICommandHandlerAsync<TAggregatePayload, TCommand> asyncHandler:
            {
                await foreach (var eventPayload in asyncHandler.HandleCommandAsync(command, this))
                {
                    _events.Add(EventHelper.HandleEvent(_aggregate, eventPayload, rootPartitionKey));
                }
                return new CommandResponse(
                    _aggregate.AggregateId,
                    _events.ToImmutableList(),
                    _aggregate.Version,
                    _events.Max(m => m.SortableUniqueId));
            }
            default:
                throw new SekibanCommandHandlerNotMatchException(
                    handler.GetType().Name + " handler should inherit " + typeof(ICommandHandler<,>).Name);
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
