using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface IQueryExecutor
{
    public Task<TQueryResponse>
        ForMultiProjectionQueryAsync<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param)
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameterCommon;

    public Task<ListQueryResult<TQueryResponse>>
        ForMultiProjectionListQueryAsync<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param)
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionListQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameterCommon;

    public Task<ListQueryResult<TQueryResponse>>
        ForAggregateListQueryAsync<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameterCommon, IListQueryInput<TQueryResponse>
        where TQueryResponse : IQueryOutput;

    public Task<TQueryResponse> ForAggregateQueryAsync<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameterCommon, IQueryInput<TQueryResponse>
        where TQueryResponse : IQueryOutput;

    public Task<ListQueryResult<TQueryResponse>>
        ForSingleProjectionListQueryAsync<TSingleProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameterCommon;

    public Task<TQueryResponse>
        ForSingleProjectionQueryAsync<TSingleProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameterCommon;

    public Task<ListQueryResult<TOutput>> ExecuteAsync<TOutput>(IListQueryInput<TOutput> param) where TOutput : IQueryOutput;
    public Task<TOutput> ExecuteAsync<TOutput>(IQueryInput<TOutput> param) where TOutput : IQueryOutput;
}
