using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultipleProjections;
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
    public Task<QueryListResult<TQueryResponse>>
        GetMultiProjectionListQueryAsync<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param) where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionListQuery<TProjection, TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public Task<QueryListResult<TQueryResponse>>
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
    public Task<QueryListResult<TQueryResponse>>
        GetSingleProjectionListQueryAsync<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(TQueryParameter param) where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : ProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>,
        new()
        where TSingleProjectionPayload : ISingleProjectionPayload
        where TQuery : ISingleProjectionListQuery<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;

    public Task<TQueryResponse>
        GetSingleProjectionQueryAsync<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(TQueryParameter param) where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : ProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>,
        new()
        where TSingleProjectionPayload : ISingleProjectionPayload
        where TQuery : ISingleProjectionQuery<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
}