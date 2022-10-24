using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface IQueryFilterService
{
    public Task<TQueryFilterResponse>
        GetProjectionQueryFilterAsync<TProjection, TProjectionContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param) where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
        where TProjectionContents : IMultipleAggregateProjectionContents, new()
        where TQueryFilter : IProjectionQueryFilterDefinition<TProjection, TProjectionContents, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter;
    public Task<QueryFilterListResult<TQueryFilterResponse>>
        GetProjectionListQueryFilterAsync<TProjection, TProjectionContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param) where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
        where TProjectionContents : IMultipleAggregateProjectionContents, new()
        where TQueryFilter : IProjectionListQueryFilterDefinition<TProjection, TProjectionContents, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter;
    public Task<QueryFilterListResult<TQueryFilterResponse>>
        GetAggregateListQueryFilterAsync<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param) 
        where TAggregatePayload : IAggregatePayload, new()
        where TQueryFilter : IAggregateListQueryFilterDefinition<TAggregatePayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter;
    public Task<TQueryFilterResponse> GetAggregateQueryFilterAsync<TAggregatePayload, TQueryFilter,
        TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQueryFilter : IAggregateQueryFilterDefinition<TAggregatePayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter;
    public Task<QueryFilterListResult<TQueryFilterResponse>>
        GetSingleAggregateProjectionListQueryFilterAsync<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : AggregateCommonBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionPayload
        where TQueryFilter : ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, TSingleAggregateProjection,
            TSingleAggregateProjectionContents, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter;

    public Task<TQueryFilterResponse>
        GetSingleAggregateProjectionQueryFilterAsync<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : AggregateCommonBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionPayload
        where TQueryFilter : ISingleAggregateProjectionQueryFilterDefinition<TAggregate, TSingleAggregateProjection,
            TSingleAggregateProjectionContents, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter;
}
