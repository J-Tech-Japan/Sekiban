using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.History;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core;

public class SekibanExecutor(
    ICommandExecutor commandExecutor,
    IAggregateLoader aggregateLoader,
    IQueryExecutor queryExecutor,
    IServiceProvider serviceProvider) : ISekibanExecutor
{
    public ResultBox<T1> GetRequiredService<T1>() where T1 : class => ResultBox.WrapTry(serviceProvider.GetRequiredService<T1>);

    public Task<ResultBox<CommandExecutorResponse>> ExecuteCommand<TCommand>(TCommand command, List<CallHistory>? callHistories = null)
        where TCommand : ICommandCommon =>
        commandExecutor.ExecCommandWithResultAsync(command, callHistories);
    public Task<ResultBox<CommandExecutorResponse>> ExecuteCommandWithoutValidation<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TCommand : ICommandCommon =>
        commandExecutor.ExecCommandWithoutValidationWithResultAsync(command, callHistories);
    public Task<ResultBox<CommandExecutorResponseWithEvents>> ExecuteCommandWithoutValidationWithEvents<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TCommand : ICommandCommon =>
        commandExecutor.ExecCommandWithoutValidationWithEventsWithResultAsync(command, callHistories);

    public Task<ResultBox<CommandExecutorResponseWithEvents>> ExecuteCommandWithEvents<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TCommand : ICommandCommon =>
        commandExecutor.ExecCommandWithEventsWithResultAsync(command, callHistories);

    public Task<ResultBox<AggregateState<TAggregatePayloadCommon>>> GetAggregateState<TAggregatePayloadCommon>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        SingleProjectionRetrievalOptions? retrievalOptions = null) where TAggregatePayloadCommon : IAggregatePayloadCommon =>
        aggregateLoader.AsDefaultStateWithResultAsync<TAggregatePayloadCommon>(aggregateId, rootPartitionKey, toVersion, retrievalOptions);

    public Task<ResultBox<AggregateState<TAggregatePayloadCommon>>> GetAggregateStateFromInitial<TAggregatePayloadCommon>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null) where TAggregatePayloadCommon : IAggregatePayloadCommon =>
        aggregateLoader.AsDefaultStateFromInitialWithResultAsync<TAggregatePayloadCommon>(aggregateId, rootPartitionKey, toVersion);

    public Task<ResultBox<SingleProjectionState<TSingleProjectionPayload>>> GetSingleProjectionStateFromInitial<TSingleProjectionPayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        ResultBox.CheckNullWrapTry(
            () => aggregateLoader.AsSingleProjectionStateFromInitialAsync<TSingleProjectionPayload>(aggregateId, rootPartitionKey, toVersion));
    public Task<ResultBox<SingleProjectionState<TSingleProjectionPayload>>> GetSingleProjectionState<TSingleProjectionPayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        SingleProjectionRetrievalOptions? retrievalOptions = null) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon =>
        ResultBox.CheckNullWrapTry(
            () => aggregateLoader.AsSingleProjectionStateAsync<TSingleProjectionPayload>(aggregateId, rootPartitionKey, toVersion, retrievalOptions));
    public Task<ResultBox<TOutput>> ExecuteQuery<TOutput>(INextQueryCommon<TOutput> query) where TOutput : notnull =>
        queryExecutor.ExecuteAsync(query);
    public Task<ResultBox<ListQueryResult<TOutput>>> ExecuteQuery<TOutput>(INextListQueryCommon<TOutput> query) where TOutput : notnull =>
        queryExecutor.ExecuteAsync(query);
    public Task<ResultBox<ListQueryResult<TOutput>>> ExecuteQuery<TOutput>(IListQueryInput<TOutput> param) where TOutput : IQueryResponse =>
        queryExecutor.ExecuteWithResultAsync(param);
    public Task<ResultBox<TOutput>> ExecuteQuery<TOutput>(IQueryInput<TOutput> param) where TOutput : IQueryResponse =>
        queryExecutor.ExecuteWithResultAsync(param);
}
