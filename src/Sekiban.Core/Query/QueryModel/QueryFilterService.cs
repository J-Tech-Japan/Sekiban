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
    public async Task<QueryFilterListResult<TQueryFilterResponse>> GetAggregateListQueryFilterAsync<TAggregate, TAggregateContents, TQueryFilter,
        TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : AggregateBase<TAggregateContents>
        where TAggregateContents : IAggregateContents, new()
        where TQueryFilter : IAggregateListQueryFilterDefinition<TAggregate, TAggregateContents, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService.GetAggregateList<TAggregate, TAggregateContents>();
        return _queryFilterHandler
            .GetAggregateListQueryFilter<TAggregate, TAggregateContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
                param,
                allProjection);
    }
    public async Task<QueryFilterListResult<TQueryFilterResponse>>
        GetSingleAggregateProjectionListQueryFilterAsync<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : AggregateCommonBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
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
    public async Task<TQueryFilterResponse> GetAggregateQueryFilterAsync<TAggregate, TAggregateContents, TQueryFilter,
        TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : AggregateBase<TAggregateContents>
        where TAggregateContents : IAggregateContents, new()
        where TQueryFilter : IAggregateQueryFilterDefinition<TAggregate, TAggregateContents, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService.GetAggregateList<TAggregate, TAggregateContents>();
        return _queryFilterHandler.GetAggregateQueryFilter<TAggregate, TAggregateContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            param,
            allProjection);
    }

    public async Task<TQueryFilterResponse>
        GetSingleAggregateProjectionQueryFilterAsync<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : AggregateCommonBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
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
