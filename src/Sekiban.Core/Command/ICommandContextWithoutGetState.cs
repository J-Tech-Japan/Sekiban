using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Command;

public interface ICommandContextWithoutGetState<TAggregatePayload> where TAggregatePayload : IAggregatePayloadCommon
{
    public ResultBox<T1> GetRequiredService<T1>() where T1 : class;
    public ResultBox<TwoValues<T1, T2>> GetRequiredService<T1, T2>() where T1 : class where T2 : class;
    public ResultBox<ThreeValues<T1, T2, T3>> GetRequiredService<T1, T2, T3>()
        where T1 : class where T2 : class where T3 : class;
    public ResultBox<FourValues<T1, T2, T3, T4>> GetRequiredService<T1, T2, T3, T4>()
        where T1 : class where T2 : class where T3 : class where T4 : class;

    public Task<ResultBox<AggregateState<TAnotherAggregatePayload>>> GetAggregateState<TAnotherAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        SingleProjectionRetrievalOptions? retrievalOptions = null)
        where TAnotherAggregatePayload : IAggregatePayloadCommon;

    public Task<ResultBox<AggregateState<TAnotherAggregatePayload>>>
        GetAggregateStateFromInitial<TAnotherAggregatePayload>(
            Guid aggregateId,
            string rootPartitionKey = IDocument.DefaultRootPartitionKey,
            int? toVersion = null) where TAnotherAggregatePayload : IAggregatePayloadCommon;

    public Task<ResultBox<SingleProjectionState<TSingleProjectionPayload>>>
        GetSingleProjectionStateFromInitialAsync<TSingleProjectionPayload>(
            Guid aggregateId,
            string rootPartitionKey = IDocument.DefaultRootPartitionKey,
            int? toVersion = null) where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;

    public Task<ResultBox<SingleProjectionState<TSingleProjectionPayload>>>
        GetSingleProjectionStateAsync<TSingleProjectionPayload>(
            Guid aggregateId,
            string rootPartitionKey = IDocument.DefaultRootPartitionKey,
            int? toVersion = null,
            SingleProjectionRetrievalOptions? retrievalOptions = null)
        where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon;

    public Task<ResultBox<TOutput>> ExecuteQueryAsync<TOutput>(INextQueryCommon<TOutput> query) where TOutput : notnull;
    public Task<ResultBox<ListQueryResult<TOutput>>> ExecuteQueryAsync<TOutput>(INextListQueryCommon<TOutput> query)
        where TOutput : notnull;

    public Task<ResultBox<ListQueryResult<TOutput>>> ExecuteQueryAsync<TOutput>(IListQueryInput<TOutput> param)
        where TOutput : IQueryResponse;

    public Task<ResultBox<TOutput>> ExecuteQueryAsync<TOutput>(IQueryInput<TOutput> param)
        where TOutput : IQueryResponse;

    public ResultBox<UnitValue> AppendEvent(IEventPayloadApplicableTo<TAggregatePayload> eventPayload);
    public ResultBox<UnitValue> AppendEvents(params IEventPayloadApplicableTo<TAggregatePayload>[] eventPayloads);
}
