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
        GetProjectionQueryFilterAsync<TProjection, TProjectionContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param) where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
        where TProjectionContents : IMultipleAggregateProjectionContents, new()
        where TQueryFilter : IProjectionQueryFilterDefinition<TProjection, TProjectionContents, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService.GetProjectionAsync<TProjection, TProjectionContents>();
        return _queryFilterHandler
            .GetProjectionQueryFilter<TProjection, TProjectionContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
                param,
                allProjection);
    }
    public async Task<QueryFilterListResult<TQueryFilterResponse>>
        GetProjectionListQueryFilterAsync<TProjection, TProjectionContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param) where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
        where TProjectionContents : IMultipleAggregateProjectionContents, new()
        where TQueryFilter : IProjectionListQueryFilterDefinition<TProjection, TProjectionContents, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService.GetProjectionAsync<TProjection, TProjectionContents>();
        return _queryFilterHandler
            .GetProjectionListQueryFilter<TProjection, TProjectionContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
                param,
                allProjection);
    }
    public async Task<QueryFilterListResult<TQueryFilterResponse>> GetAggregateListQueryFilterAsync<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregatePayload : IAggregatePayload, new() where TQueryFilter : IAggregateListQueryFilterDefinition<TAggregatePayload, TQueryFilterParameter, TQueryFilterResponse> where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService.GetAggregateList< TAggregatePayload>();
        return _queryFilterHandler
            .GetAggregateListQueryFilter<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
                param,
                allProjection);
    }
    public async Task<TQueryFilterResponse> GetAggregateQueryFilterAsync<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregatePayload : IAggregatePayload, new() where TQueryFilter : IAggregateQueryFilterDefinition<TAggregatePayload, TQueryFilterParameter, TQueryFilterResponse> where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService.GetAggregateList<TAggregatePayload>();
        return _queryFilterHandler.GetAggregateQueryFilter<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            param,
            allProjection);
    }
    public async Task<QueryFilterListResult<TQueryFilterResponse>>
        GetSingleAggregateProjectionListQueryFilterAsync<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : IAggregatePayload, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionPayload
        where TQueryFilter : ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, TSingleAggregateProjection,
            TSingleAggregateProjectionContents, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService
            .GetSingleAggregateProjectionList<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>();
        return _queryFilterHandler
            .GetSingleAggregateProjectionListQueryFilter<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents, TQueryFilter,
                TQueryFilterParameter, TQueryFilterResponse>(param, allProjection);
    }
    public async Task<TQueryFilterResponse>
        GetSingleAggregateProjectionQueryFilterAsync<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : IAggregatePayload, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionPayload
        where TQueryFilter : ISingleAggregateProjectionQueryFilterDefinition<TAggregate, TSingleAggregateProjection,
            TSingleAggregateProjectionContents, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService
            .GetSingleAggregateProjectionList<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>();
        return _queryFilterHandler
            .GetSingleAggregateProjectionQueryFilter<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents, TQueryFilter,
                TQueryFilterParameter, TQueryFilterResponse>(param, allProjection);
    }
}
