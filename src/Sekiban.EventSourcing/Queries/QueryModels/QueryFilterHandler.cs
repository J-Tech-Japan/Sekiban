using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.QueryModels;

public class QueryFilterHandler
{
    private readonly IServiceProvider _serviceProvider;

    public QueryFilterHandler(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public QueryFilterListResult<TQueryFilterResponse>
        GetProjectionListQueryFilter<TProjection, TProjectionContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param,
            MultipleAggregateProjectionContentsDto<TProjectionContents> projection)
        where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
        where TProjectionContents : IMultipleAggregateProjectionContents, new()
        where TQueryFilter : IProjectionListQueryFilterDefinition<TProjection, TProjectionContents, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = _serviceProvider.GetService<TQueryFilter>();
        if (queryFilter is null) { throw new Exception($"QueryFilter {typeof(TQueryFilter).FullName} is not registered to dependency injection"); }
        var filtered = queryFilter.HandleFilter(param, projection);
        var sorted = queryFilter.HandleSort(param, filtered);
        var queryFilterResponses = sorted.ToList();
        if (param is IQueryPagingParameter { PageNumber: { }, PageSize: { } } pagingParam)
        {
            return makeQueryFilterListResult(pagingParam, queryFilterResponses);
        }
        return new QueryFilterListResult<TQueryFilterResponse>(queryFilterResponses.ToList().Count, null, null, null, queryFilterResponses);
    }

    public TQueryFilterResponse GetProjectionQueryFilter<TProjection, TProjectionContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
        TQueryFilterParameter param,
        MultipleAggregateProjectionContentsDto<TProjectionContents> projection)
        where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
        where TProjectionContents : IMultipleAggregateProjectionContents, new()
        where TQueryFilter : IProjectionQueryFilterDefinition<TProjection, TProjectionContents, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = _serviceProvider.GetService<TQueryFilter>();
        if (queryFilter is null) { throw new Exception($"QueryFilter {typeof(TQueryFilter).FullName} is not registered to dependency injection"); }
        var filtered = queryFilter.HandleFilter(param, projection);
        return filtered;
    }


    public QueryFilterListResult<TQueryFilterResponse>
        GetAggregateListQueryFilter<TAggregate, TAggregateContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param,
            IEnumerable<AggregateDto<TAggregateContents>> list) where TAggregate : AggregateBase<TAggregateContents>
        where TAggregateContents : IAggregateContents, new()
        where TQueryFilter : IAggregateListQueryFilterDefinition<TAggregate, TAggregateContents, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = _serviceProvider.GetService<TQueryFilter>();
        if (queryFilter is null) { throw new Exception($"QueryFilter {typeof(TQueryFilter).FullName} is not registered to dependency injection"); }
        var filtered = queryFilter.HandleFilter(param, list);
        var sorted = queryFilter.HandleSort(param, filtered);
        var queryFilterResponses = sorted.ToList();
        if (param is IQueryPagingParameter { PageNumber: { }, PageSize: { } } pagingParam)
        {
            return makeQueryFilterListResult(pagingParam, queryFilterResponses);
        }
        return new QueryFilterListResult<TQueryFilterResponse>(queryFilterResponses.ToList().Count, null, null, null, queryFilterResponses);
    }
    public TQueryFilterResponse GetAggregateQueryFilter<TAggregate, TAggregateContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
        TQueryFilterParameter param,
        IEnumerable<AggregateDto<TAggregateContents>> list) where TAggregate : AggregateBase<TAggregateContents>
        where TAggregateContents : IAggregateContents, new()
        where TQueryFilter : IAggregateQueryFilterDefinition<TAggregate, TAggregateContents, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = _serviceProvider.GetService<TQueryFilter>();
        if (queryFilter is null) { throw new Exception($"QueryFilter {typeof(TQueryFilter).FullName} is not registered to dependency injection"); }
        var filtered = queryFilter.HandleFilter(param, list);
        return filtered;
    }

    public QueryFilterListResult<TQueryFilterResponse>
        GetSingleAggregateProjectionListQueryFilter<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param,
            IEnumerable<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>> projections) where TAggregate : AggregateCommonBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
        where TQueryFilter : ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, TSingleAggregateProjection,
            TSingleAggregateProjectionContents, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = _serviceProvider.GetService<TQueryFilter>();
        if (queryFilter is null) { throw new Exception($"QueryFilter {typeof(TQueryFilter).FullName} is not registered to dependency injection"); }
        var filtered = queryFilter.HandleFilter(param, projections);
        var sorted = queryFilter.HandleSort(param, filtered);
        var queryFilterResponses = sorted.ToList();
        if (param is IQueryPagingParameter { PageNumber: { }, PageSize: { } } pagingParam)
        {
            return makeQueryFilterListResult(pagingParam, queryFilterResponses);
        }
        return new QueryFilterListResult<TQueryFilterResponse>(queryFilterResponses.ToList().Count, null, null, null, queryFilterResponses);
    }

    private static QueryFilterListResult<TQueryFilterResponse> makeQueryFilterListResult<TQueryFilterResponse>(
        IQueryPagingParameter pagingParam,
        List<TQueryFilterResponse> queryFilterResponses)
    {
        if (pagingParam.PageNumber == null || pagingParam.PageSize == null)
        {
            throw new SekibanQueryFilterPagingError();
        }
        var pageNumber = pagingParam.PageNumber.Value;
        var pageSize = pagingParam.PageSize.Value;
        var total = queryFilterResponses.ToList().Count;
        var totalPages = total / pagingParam.PageSize.Value + (total % pagingParam.PageSize.Value > 0 ? 1 : 0);
        if (pageNumber < 1 || pageNumber > totalPages)
        {
            return new QueryFilterListResult<TQueryFilterResponse>(total, totalPages, pageNumber, pageSize, new List<TQueryFilterResponse>());
        }
        return new QueryFilterListResult<TQueryFilterResponse>(
            total,
            totalPages,
            pageNumber,
            pageSize,
            queryFilterResponses.Skip((pageNumber - 1) * pagingParam.PageSize.Value).Take(pageSize));
    }

    public TQueryFilterResponse
        GetSingleAggregateProjectionQueryFilter<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param,
            IEnumerable<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>> projections) where TAggregate : AggregateCommonBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>,
        new()
        where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
        where TQueryFilter : ISingleAggregateProjectionQueryFilterDefinition<TAggregate, TSingleAggregateProjection,
            TSingleAggregateProjectionContents, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = _serviceProvider.GetService<TQueryFilter>();
        if (queryFilter is null) { throw new Exception($"QueryFilter {typeof(TQueryFilter).FullName} is not registered to dependency injection"); }
        var filtered = queryFilter.HandleFilter(param, projections);
        return filtered;
    }
}
