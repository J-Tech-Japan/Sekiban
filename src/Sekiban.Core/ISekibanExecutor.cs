using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.History;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Usecase;
namespace Sekiban.Core;

public interface ISekibanExecutor
{
    public ResultBox<T1> GetRequiredService<T1>() where T1 : class;

    Task<ResultBox<CommandExecutorResponse>> ExecuteCommand<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TCommand : ICommandCommon;

    Task<ResultBox<CommandExecutorResponse>> ExecuteCommandWithoutValidation<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TCommand : ICommandCommon;
    Task<ResultBox<CommandExecutorResponseWithEvents>> ExecuteCommandWithoutValidationWithEvents<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TCommand : ICommandCommon;

    Task<ResultBox<CommandExecutorResponseWithEvents>> ExecuteCommandWithEvents<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TCommand : ICommandCommon;
    public Task<ResultBox<AggregateState<TAggregatePayloadCommon>>> GetAggregateState<TAggregatePayloadCommon>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        SingleProjectionRetrievalOptions? retrievalOptions = null)
        where TAggregatePayloadCommon : IAggregatePayloadCommon;

    public Task<ResultBox<AggregateState<TAggregatePayloadCommon>>>
        GetAggregateStateFromInitial<TAggregatePayloadCommon>(
            Guid aggregateId,
            string rootPartitionKey = IDocument.DefaultRootPartitionKey,
            int? toVersion = null) where TAggregatePayloadCommon : IAggregatePayloadCommon;

    public Task<ResultBox<SingleProjectionState<TSingleProjectionPayload>>>
        GetSingleProjectionStateFromInitial<TSingleProjectionPayload>(
            Guid aggregateId,
            string rootPartitionKey = IDocument.DefaultRootPartitionKey,
            int? toVersion = null) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;

    public Task<ResultBox<SingleProjectionState<TSingleProjectionPayload>>>
        GetSingleProjectionState<TSingleProjectionPayload>(
            Guid aggregateId,
            string rootPartitionKey = IDocument.DefaultRootPartitionKey,
            int? toVersion = null,
            SingleProjectionRetrievalOptions? retrievalOptions = null)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;

    public Task<ResultBox<TOutput>> ExecuteQuery<TOutput>(INextQueryCommonOutput<TOutput> query)
        where TOutput : notnull;
    public Task<ResultBox<ListQueryResult<TOutput>>> ExecuteQuery<TOutput>(INextListQueryCommonOutput<TOutput> query)
        where TOutput : notnull;

    public Task<ResultBox<ListQueryResult<TOutput>>> ExecuteQuery<TOutput>(IListQueryInput<TOutput> param)
        where TOutput : IQueryResponse;

    public Task<ResultBox<TOutput>> ExecuteQuery<TOutput>(IQueryInput<TOutput> param) where TOutput : IQueryResponse;



    public Task<ResultBox<TOut>> ExecuteUsecase<TIn, TOut>(ISekibanUsecaseAsync<TIn, TOut> usecaseAsync)
        where TIn : class, ISekibanUsecaseAsync<TIn, TOut>, IEquatable<TIn> where TOut : notnull;

    public ResultBox<TOut> ExecuteUsecase<TIn, TOut>(ISekibanUsecase<TIn, TOut> usecaseAsync)
        where TIn : class, IEquatable<TIn>, ISekibanUsecase<TIn, TOut> where TOut : notnull;
    public ResultBox<TwoValues<T1, T2>> GetRequiredService<T1, T2>() where T1 : class where T2 : class;

    public ResultBox<ThreeValues<T1, T2, T3>> GetRequiredService<T1, T2, T3>()
        where T1 : class where T2 : class where T3 : class;

    public ResultBox<FourValues<T1, T2, T3, T4>> GetRequiredService<T1, T2, T3, T4>()
        where T1 : class where T2 : class where T3 : class where T4 : class;
}
