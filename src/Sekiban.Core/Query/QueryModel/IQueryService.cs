using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultipleProjections;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface IQueryService
{
    public Task<TQueryFilterResponse>
        GetMultiProjectionQueryFilterAsync<TProjection, TProjectionPayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param) where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQueryFilter : IMultiProjectionQuery<TProjection, TProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter;
    public Task<QueryListResult<TQueryFilterResponse>>
        GetMultiProjectionListQueryAsync<TProjection, TProjectionPayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param) where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQueryFilter : IMultiProjectionListQuery<TProjection, TProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter;
    public Task<QueryListResult<TQueryFilterResponse>>
        GetAggregateListQueryAsync<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQueryFilter : IAggregateListQuery<TAggregatePayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter;
    public Task<TQueryFilterResponse> GetAggregateQueryAsync<TAggregatePayload, TQueryFilter,
        TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQueryFilter : IAggregateQuery<TAggregatePayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter;
    public Task<QueryListResult<TQueryFilterResponse>>
        GetSingleProjectionListQueryAsync<TAggregate, TSingleProjection, TAggregateProjectionPayload, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregate, TSingleProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleProjectionPayload
        where TQueryFilter : ISingleProjectionListQuery<TAggregate, TSingleProjection,
            TAggregateProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter;

    public Task<TQueryFilterResponse>
        GetSingleProjectionQueryAsync<TAggregate, TSingleProjection, TAggregateProjectionPayload, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregate, TSingleProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleProjectionPayload
        where TQueryFilter : ISingleProjectionQuery<TAggregate, TSingleProjection,
            TAggregateProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter;
}
