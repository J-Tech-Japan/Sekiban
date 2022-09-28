using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.QueryModels;

public class QueryFilterService : IQueryFilterService
{
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    public QueryFilterService(IMultipleAggregateProjectionService multipleAggregateProjectionService)
    {
        _multipleAggregateProjectionService = multipleAggregateProjectionService;
    }
    public async Task<TQueryFilterResponse>
        GetProjectionQueryFilterAsync<TProjection, TProjectionContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param) where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
        where TProjectionContents : IMultipleAggregateProjectionContents, new()
        where TQueryFilter : IProjectionQueryFilterDefinition<TProjection, TProjectionContents, TQueryFilterParameter, TQueryFilterResponse>, new()
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService.GetProjectionAsync<TProjection, TProjectionContents>();
        var queryFilter = new TQueryFilter();
        var filtered = queryFilter.HandleFilter(param, allProjection);
        return queryFilter.HandleSortAndPagingIfNeeded(param, filtered);
    }
    public async Task<IEnumerable<TQueryFilterResponse>>
        GetProjectionListQueryFilterAsync<TProjection, TProjectionContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param) where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
        where TProjectionContents : IMultipleAggregateProjectionContents, new()
        where TQueryFilter : IProjectionListQueryFilterDefinition<TProjection, TProjectionContents, TQueryFilterParameter, TQueryFilterResponse>, new
        ()
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService.GetProjectionAsync<TProjection, TProjectionContents>();
        var queryFilter = new TQueryFilter();
        var filtered = queryFilter.HandleFilter(param, allProjection);
        if (param is IQueryPagingParameter { PageNumber: { }, PageSize: { } } pagingParam)
        {
            filtered = filtered.Skip((pagingParam.PageNumber.Value - 1) * pagingParam.PageSize.Value).Take(pagingParam.PageSize.Value);
        }
        return queryFilter.HandleSort(param, filtered);
    }
    public async Task<IEnumerable<TQueryFilterResponse>> GetAggregateListQueryFilterAsync<TAggregate, TAggregateContents, TQueryFilter,
        TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : TransferableAggregateBase<TAggregateContents>
        where TAggregateContents : IAggregateContents, new()
        where TQueryFilter : IAggregateListQueryFilterDefinition<TAggregate, TAggregateContents, TQueryFilterParameter, TQueryFilterResponse>, new()
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService.GetAggregateList<TAggregate, TAggregateContents>();
        var queryFilter = new TQueryFilter();
        var filtered = queryFilter.HandleFilter(param, allProjection);
        if (param is IQueryPagingParameter { PageNumber: { }, PageSize: { } } pagingParam)
        {
            filtered = filtered.Skip((pagingParam.PageNumber.Value - 1) * pagingParam.PageSize.Value).Take(pagingParam.PageSize.Value);
        }
        return queryFilter.HandleSort(param, filtered);
    }
    public async Task<IEnumerable<TQueryFilterResponse>>
        GetSingleAggregateProjectionListQueryFilterAsync<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : AggregateBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
        where TQueryFilter : ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, TSingleAggregateProjection,
            TSingleAggregateProjectionContents, TQueryFilterParameter, TQueryFilterResponse>, new()
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService
            .GetSingleAggregateProjectionList<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>();
        var queryFilter = new TQueryFilter();
        var filtered = queryFilter.HandleFilter(param, allProjection);
        if (param is IQueryPagingParameter { PageNumber: { }, PageSize: { } } pagingParam)
        {
            filtered = filtered.Skip((pagingParam.PageNumber.Value - 1) * pagingParam.PageSize.Value).Take(pagingParam.PageSize.Value);
        }
        return queryFilter.HandleSort(param, filtered);
    }
}
