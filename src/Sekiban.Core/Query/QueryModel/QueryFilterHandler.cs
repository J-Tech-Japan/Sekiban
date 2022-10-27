using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Query.QueryModel;

public class QueryFilterHandler
{
    private readonly IServiceProvider _serviceProvider;

    public QueryFilterHandler(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public QueryFilterListResult<TQueryFilterResponse>
        GetProjectionListQueryFilter<TProjection, TProjectionPayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param,
            MultipleAggregateProjectionState<TProjectionPayload> projection)
        where TProjection : MultipleAggregateProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultipleAggregateProjectionPayload, new()
        where TQueryFilter : IProjectionListQueryFilterDefinition<TProjection, TProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
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

    public TQueryFilterResponse GetProjectionQueryFilter<TProjection, TProjectionPayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
        TQueryFilterParameter param,
        MultipleAggregateProjectionState<TProjectionPayload> projection)
        where TProjection : MultipleAggregateProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultipleAggregateProjectionPayload, new()
        where TQueryFilter : IProjectionQueryFilterDefinition<TProjection, TProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = _serviceProvider.GetService<TQueryFilter>();
        if (queryFilter is null) { throw new Exception($"QueryFilter {typeof(TQueryFilter).FullName} is not registered to dependency injection"); }
        var filtered = queryFilter.HandleFilter(param, projection);
        return filtered;
    }


    public QueryFilterListResult<TQueryFilterResponse>
        GetAggregateListQueryFilter<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param,
            IEnumerable<AggregateState<TAggregatePayload>> list)
        where TAggregatePayload : IAggregatePayload, new()
        where TQueryFilter : IAggregateListQueryFilterDefinition<TAggregatePayload, TQueryFilterParameter, TQueryFilterResponse>
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
    public TQueryFilterResponse GetAggregateQueryFilter<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
        TQueryFilterParameter param,
        IEnumerable<AggregateState<TAggregatePayload>> list)
        where TAggregatePayload : IAggregatePayload, new()
        where TQueryFilter : IAggregateQueryFilterDefinition<TAggregatePayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = _serviceProvider.GetService<TQueryFilter>();
        if (queryFilter is null) { throw new Exception($"QueryFilter {typeof(TQueryFilter).FullName} is not registered to dependency injection"); }
        var filtered = queryFilter.HandleFilter(param, list);
        return filtered;
    }

    public QueryFilterListResult<TQueryFilterResponse>
        GetSingleAggregateProjectionListQueryFilter<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param,
            IEnumerable<SingleAggregateProjectionState<TAggregateProjectionPayload>> projections) where TAggregate : IAggregatePayload, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleAggregateProjectionPayload
        where TQueryFilter : ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, TSingleAggregateProjection,
            TAggregateProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
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
        GetSingleAggregateProjectionQueryFilter<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param,
            IEnumerable<SingleAggregateProjectionState<TAggregateProjectionPayload>> projections) where TAggregate : IAggregatePayload, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleAggregateProjectionPayload
        where TQueryFilter : ISingleAggregateProjectionQueryFilterDefinition<TAggregate, TSingleAggregateProjection,
            TAggregateProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = _serviceProvider.GetService<TQueryFilter>();
        if (queryFilter is null) { throw new Exception($"QueryFilter {typeof(TQueryFilter).FullName} is not registered to dependency injection"); }
        var filtered = queryFilter.HandleFilter(param, projections);
        return filtered;
    }
}
