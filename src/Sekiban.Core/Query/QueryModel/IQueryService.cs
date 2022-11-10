using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface IQueryService
{
    public Task<TQueryResponse>
        GetMultiProjectionQueryAsync<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param) where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionQuery<TProjection, TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public Task<ListQueryResult<TQueryResponse>>
        GetMultiProjectionListQueryAsync<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param) where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionListQuery<TProjection, TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public Task<ListQueryResult<TQueryResponse>>
        GetAggregateListQueryAsync<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public Task<TQueryResponse> GetAggregateQueryAsync<TAggregatePayload, TQuery,
        TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public Task<ListQueryResult<TQueryResponse>>
        GetSingleProjectionListQueryAsync<TSingleProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;

    public Task<TQueryResponse>
        GetSingleProjectionQueryAsync<TSingleProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
}
