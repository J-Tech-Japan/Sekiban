using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Types;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

public class
    StaticCommandWithoutLoadingAggregateHandlerAdapter<TAggregatePayload, TCommand>(IServiceProvider serviceProvider)
    : ICommandContextWithoutGetState<TAggregatePayload>
    where TAggregatePayload : IAggregatePayloadGeneratable<TAggregatePayload>
    where TCommand : ICommandWithHandlerCommon<TAggregatePayload, TCommand>, ICommandWithoutLoadingAggregateCommon
{
    private readonly List<IEvent> _events = [];
    private Guid _aggregateId = Guid.Empty;
    private string _rootPartitionKey = string.Empty;
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
    public Task<ResultBox<TOutput>> ExecuteQueryAsync<TOutput>(INextQueryCommonOutput<TOutput> query)
        where TOutput : notnull =>
        GetRequiredService<IQueryExecutor>().Conveyor(executor => executor.ExecuteAsync(query));
    public Task<ResultBox<ListQueryResult<TOutput>>> ExecuteQueryAsync<TOutput>(
        INextListQueryCommonOutput<TOutput> query) where TOutput : notnull =>
        GetRequiredService<IQueryExecutor>().Conveyor(executor => executor.ExecuteAsync(query));
    public Task<ResultBox<ListQueryResult<TOutput>>> ExecuteQueryAsync<TOutput>(IListQueryInput<TOutput> param)
        where TOutput : IQueryResponse =>
        GetRequiredService<IQueryExecutor>().Conveyor(executor => executor.ExecuteWithResultAsync(param));
    public Task<ResultBox<TOutput>> ExecuteQueryAsync<TOutput>(IQueryInput<TOutput> param)
        where TOutput : IQueryResponse =>
        GetRequiredService<IQueryExecutor>().Conveyor(executor => executor.ExecuteWithResultAsync(param));


    public ResultBox<EventOrNone<TAggregatePayload>> AppendEvent(
        IEventPayloadApplicableTo<TAggregatePayload> eventPayload) =>
        ResultBox
            .Start
            .Scan(
                aggregate => _events.Add(
                    EventHelper.GenerateEventToSave<IEventPayloadApplicableTo<TAggregatePayload>, TAggregatePayload>(
                        _aggregateId,
                        _rootPartitionKey,
                        eventPayload)))
            .Conveyor(_ => EventOrNone<TAggregatePayload>.None);
    public ResultBox<EventOrNone<TAggregatePayload>> AppendEvents(
        params IEventPayloadApplicableTo<TAggregatePayload>[] eventPayloads) =>
        ResultBox
            .FromValue(eventPayloads.ToList())
            .ReduceEach(EventOrNone<TAggregatePayload>.Empty, (nextEventPayload, _) => AppendEvent(nextEventPayload));
    public async Task<ResultBox<CommandResponse>> HandleCommandAsync(
        CommandDocument<TCommand> commandDocument,
        Guid aggregateId,
        string rootPartitionKey)
    {
        _rootPartitionKey = rootPartitionKey;
        _aggregateId = aggregateId;
        switch (commandDocument.Payload)
        {
            case ICommandWithHandlerWithoutLoadingAggregateAbove<TAggregatePayload, TCommand> syncCommand:
            {
                var commandType = syncCommand.GetType();
                var method = commandType.GetHandleCommandOrAsyncMethod();
                if (method is null)
                {
                    return ResultBox<CommandResponse>.FromException(
                        new SekibanCommandHandlerNotMatchException(
                            commandType.Name + " handler should inherit " + typeof(ICommandWithHandler<,>).Name));
                }
                var result
                    = method.Invoke(null, new object[] { syncCommand, this }) as
                        ResultBox<EventOrNone<TAggregatePayload>>;
                if (result is null)
                {
                    return ResultBox<CommandResponse>.FromException(
                        new SekibanCommandHandlerNotMatchException(
                            commandType.Name + " handler should inherit " + typeof(ICommandWithHandler<,>).Name));
                }
                if (result.IsSuccess && result.GetValue().HasValue)
                {
                    AppendEvent(result.GetValue().GetValue());
                }
                result.UnwrapBox();
                return new CommandResponse(
                    aggregateId,
                    _events.ToImmutableList(),
                    0,
                    _events.Max(m => m.SortableUniqueId));
            }
            case ICommandWithHandlerWithoutLoadingAggregateAsyncAbove<TAggregatePayload, TCommand> asyncCommand:
            {
                var commandType = asyncCommand.GetType();
                var method = commandType.GetHandleCommandOrAsyncMethod();
                if (method is null)
                {
                    return ResultBox<CommandResponse>.FromException(
                        new SekibanCommandHandlerNotMatchException(
                            commandType.Name + " handler should inherit " + typeof(ICommandWithHandler<,>).Name));
                }
                var resultTask
                    = method.Invoke(null, new object[] { asyncCommand, this }) as
                        Task<ResultBox<EventOrNone<TAggregatePayload>>>;
                if (resultTask is null)
                {
                    return ResultBox<CommandResponse>.FromException(
                        new SekibanCommandHandlerNotMatchException(
                            commandType.Name + " handler should inherit " + typeof(ICommandWithHandler<,>).Name));
                }
                var result = await resultTask;
                if (result.IsSuccess && result.GetValue().HasValue)
                {
                    AppendEvent(result.GetValue().GetValue());
                }
                result.UnwrapBox();
                return new CommandResponse(
                    aggregateId,
                    _events.ToImmutableList(),
                    0,
                    _events.Max(m => m.SortableUniqueId));
            }
            default:
                return ResultBox<CommandResponse>.FromException(
                    new SekibanCommandHandlerNotMatchException(
                        commandDocument.Payload.GetType().Name +
                        " handler should inherit " +
                        typeof(ICommandWithoutLoadingAggregateHandler<,>).Name));
        }
    }
}
