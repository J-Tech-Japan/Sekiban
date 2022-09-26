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
        GetProjectionQueryFilterAsync<TProjection, TQueryFilter, TQueryFilterParam, TQueryFilterResponse>(TQueryFilterParam param)
        where TProjection : MultipleAggregateProjectionBase<TProjection>, IMultipleAggregateProjectionDto, new()
        where TQueryFilter : IProjectionQueryFilterDefinition<TProjection, TQueryFilterParam, TQueryFilterResponse>, new()
        where TQueryFilterParam : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService.GetProjectionAsync<TProjection>();
        var queryFilter = new TQueryFilter();
        var filtered = queryFilter.HandleFilter(param, allProjection);
        return queryFilter.HandleSortAndPagingIfNeeded(param, filtered);
    }
    public async Task<IEnumerable<TQueryFilterResponse>>
        GetProjectionListQueryFilterAsync<TProjection, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param)
        where TProjection : MultipleAggregateProjectionBase<TProjection>, IMultipleAggregateProjectionDto, new()
        where TQueryFilter : IProjectionListQueryFilterDefinition<TProjection, TQueryFilterParameter, TQueryFilterResponse>, new()
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService.GetProjectionAsync<TProjection>();
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
        GetSingleAggregateProjectionListQueryFilterAsync<TAggregate, TSingleAggregateProjection, TQueryFilter, TQueryFilterParameter,
            TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : AggregateBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection>, new()
        where TQueryFilter : ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, TSingleAggregateProjection, TQueryFilterParameter,
            TQueryFilterResponse>, new()
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await _multipleAggregateProjectionService.GetSingleAggregateProjectionList<TAggregate, TSingleAggregateProjection>();
        var queryFilter = new TQueryFilter();
        var filtered = queryFilter.HandleFilter(param, allProjection);
        if (param is IQueryPagingParameter { PageNumber: { }, PageSize: { } } pagingParam)
        {
            filtered = filtered.Skip((pagingParam.PageNumber.Value - 1) * pagingParam.PageSize.Value).Take(pagingParam.PageSize.Value);
        }
        return queryFilter.HandleSort(param, filtered);
    }
}
