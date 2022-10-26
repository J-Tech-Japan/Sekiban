using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Query.QueryModel;

public class QueryFilterService : IQueryFilterService
{
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    private readonly QueryFilterHandler _queryFilterHandler;
    public QueryFilterService(IMultipleAggregateProjectionService multipleAggregateProjectionService, QueryFilterHandler queryFilterHandler)
    {
        _multipleAggregateProjectionService = multipleAggregateProjectionService;
        _queryFilterHandler = queryFilterHandler;
    }
    public async Task<TQueryFilterResponse>
        GetProjectionQueryFilterAsync<TProjection, TProjectionPayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param) where TProjection : MultipleAggregateProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultipleAggregateProjectionPayload, new()
        where TQueryFilter : IProjectionQueryFilterDefinition<TProjection, TProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService.GetProjectionAsync<TProjection, TProjectionPayload>();
        return _queryFilterHandler
            .GetProjectionQueryFilter<TProjection, TProjectionPayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
                param,
                allProjection);
    }
    public async Task<QueryFilterListResult<TQueryFilterResponse>>
        GetProjectionListQueryFilterAsync<TProjection, TProjectionPayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param) where TProjection : MultipleAggregateProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultipleAggregateProjectionPayload, new()
        where TQueryFilter : IProjectionListQueryFilterDefinition<TProjection, TProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService.GetProjectionAsync<TProjection, TProjectionPayload>();
        return _queryFilterHandler
            .GetProjectionListQueryFilter<TProjection, TProjectionPayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
                param,
                allProjection);
    }
    public async Task<QueryFilterListResult<TQueryFilterResponse>>
        GetAggregateListQueryFilterAsync<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQueryFilter : IAggregateListQueryFilterDefinition<TAggregatePayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService.GetAggregateList<TAggregatePayload>();
        return _queryFilterHandler
            .GetAggregateListQueryFilter<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
                param,
                allProjection);
    }
    public async Task<TQueryFilterResponse>
        GetAggregateQueryFilterAsync<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQueryFilter : IAggregateQueryFilterDefinition<TAggregatePayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService.GetAggregateList<TAggregatePayload>();
        return _queryFilterHandler.GetAggregateQueryFilter<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            param,
            allProjection);
    }
    public async Task<QueryFilterListResult<TQueryFilterResponse>>
        GetSingleAggregateProjectionListQueryFilterAsync<TAggregate, TSingleAggregateProjection, TProjectionPayload, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : IAggregatePayload, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TProjectionPayload>,
        new()
        where TProjectionPayload : ISingleAggregateProjectionPayload
        where TQueryFilter : ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, TSingleAggregateProjection,
            TProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService
            .GetSingleAggregateProjectionList<TAggregate, TSingleAggregateProjection, TProjectionPayload>();
        return _queryFilterHandler
            .GetSingleAggregateProjectionListQueryFilter<TAggregate, TSingleAggregateProjection, TProjectionPayload, TQueryFilter,
                TQueryFilterParameter, TQueryFilterResponse>(param, allProjection);
    }
    public async Task<TQueryFilterResponse>
        GetSingleAggregateProjectionQueryFilterAsync<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : IAggregatePayload, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleAggregateProjectionPayload
        where TQueryFilter : ISingleAggregateProjectionQueryFilterDefinition<TAggregate, TSingleAggregateProjection,
            TAggregateProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService
            .GetSingleAggregateProjectionList<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>();
        return _queryFilterHandler
            .GetSingleAggregateProjectionQueryFilter<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload, TQueryFilter,
                TQueryFilterParameter, TQueryFilterResponse>(param, allProjection);
    }
}
