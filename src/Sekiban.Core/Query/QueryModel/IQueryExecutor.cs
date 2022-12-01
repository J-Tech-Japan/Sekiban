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
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;

    public Task<ListQueryResult<TQueryResponse>>
        ForMultiProjectionListQueryAsync<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param)
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionListQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;

    public Task<ListQueryResult<TQueryResponse>>
        ForAggregateListQueryAsync<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;

    public Task<TQueryResponse> ForAggregateQueryAsync<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;

    public Task<ListQueryResult<TQueryResponse>>
        ForSingleProjectionListQueryAsync<TSingleProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;

    public Task<TQueryResponse>
        ForSingleProjectionQueryAsync<TSingleProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
}
