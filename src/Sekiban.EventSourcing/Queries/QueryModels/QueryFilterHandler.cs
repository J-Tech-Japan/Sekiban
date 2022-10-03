using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
namespace Sekiban.EventSourcing.Queries.QueryModels;

public static class QueryFilterHandler
{
    public static IEnumerable<TQueryFilterResponse>
        GetProjectionListQueryFilter<TProjection, TProjectionContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param,
            MultipleAggregateProjectionContentsDto<TProjectionContents> projection)
        where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
        where TProjectionContents : IMultipleAggregateProjectionContents, new()
        where TQueryFilter : IProjectionListQueryFilterDefinition<TProjection, TProjectionContents, TQueryFilterParameter, TQueryFilterResponse>, new
        ()
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = new TQueryFilter();
        var filtered = queryFilter.HandleFilter(param, projection);
        var sorted = queryFilter.HandleSort(param, filtered);
        if (param is IQueryPagingParameter { PageNumber: { }, PageSize: { } } pagingParam)
        {
            return sorted.Skip((pagingParam.PageNumber.Value - 1) * pagingParam.PageSize.Value).Take(pagingParam.PageSize.Value);
        }
        return sorted;
    }

    public static TQueryFilterResponse
        GetProjectionQueryFilter<TProjection, TProjectionContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param,
            MultipleAggregateProjectionContentsDto<TProjectionContents> projection)
        where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
        where TProjectionContents : IMultipleAggregateProjectionContents, new()
        where TQueryFilter : IProjectionQueryFilterDefinition<TProjection, TProjectionContents, TQueryFilterParameter, TQueryFilterResponse>, new()
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = new TQueryFilter();
        var filtered = queryFilter.HandleFilter(param, projection);
        return queryFilter.HandleSortAndPagingIfNeeded(param, filtered);
    }
}
