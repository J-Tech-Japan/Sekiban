using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.MultipleProjections;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public class QueryHandler
{
    private readonly IServiceProvider _serviceProvider;

    public QueryHandler(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    public QueryListResult<TQueryFilterResponse>
        GetMultiProjectionListQuery<TProjection, TProjectionPayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param,
            MultiProjectionState<TProjectionPayload> projection)
        where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQueryFilter : IMultiProjectionListQuery<TProjection, TProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
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
        return new QueryListResult<TQueryFilterResponse>(queryFilterResponses.ToList().Count, null, null, null, queryFilterResponses);
    }

    public TQueryFilterResponse GetMultiProjectionQuery<TProjection, TProjectionPayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
        TQueryFilterParameter param,
        MultiProjectionState<TProjectionPayload> projection)
        where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQueryFilter : IMultiProjectionQuery<TProjection, TProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = _serviceProvider.GetService<TQueryFilter>();
        if (queryFilter is null) { throw new Exception($"QueryFilter {typeof(TQueryFilter).FullName} is not registered to dependency injection"); }
        var filtered = queryFilter.HandleFilter(param, projection);
        return filtered;
    }


    public QueryListResult<TQueryFilterResponse>
        GetAggregateListQuery<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param,
            IEnumerable<AggregateState<TAggregatePayload>> list)
        where TAggregatePayload : IAggregatePayload, new()
        where TQueryFilter : IAggregateListQuery<TAggregatePayload, TQueryFilterParameter, TQueryFilterResponse>
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
        return new QueryListResult<TQueryFilterResponse>(queryFilterResponses.ToList().Count, null, null, null, queryFilterResponses);
    }
    public TQueryFilterResponse GetAggregateQuery<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
        TQueryFilterParameter param,
        IEnumerable<AggregateState<TAggregatePayload>> list)
        where TAggregatePayload : IAggregatePayload, new()
        where TQueryFilter : IAggregateQuery<TAggregatePayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = _serviceProvider.GetService<TQueryFilter>();
        if (queryFilter is null) { throw new Exception($"QueryFilter {typeof(TQueryFilter).FullName} is not registered to dependency injection"); }
        var filtered = queryFilter.HandleFilter(param, list);
        return filtered;
    }

    public QueryListResult<TQueryFilterResponse>
        GetSingleProjectionListQuery<TAggregate, TSingleProjection, TAggregateProjectionPayload, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param,
            IEnumerable<SingleProjectionState<TAggregateProjectionPayload>> projections) where TAggregate : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregate, TSingleProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleProjectionPayload
        where TQueryFilter : ISingleProjectionListQuery<TAggregate, TSingleProjection,
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
        return new QueryListResult<TQueryFilterResponse>(queryFilterResponses.ToList().Count, null, null, null, queryFilterResponses);
    }

    private static QueryListResult<TQueryFilterResponse> makeQueryFilterListResult<TQueryFilterResponse>(
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
            return new QueryListResult<TQueryFilterResponse>(total, totalPages, pageNumber, pageSize, new List<TQueryFilterResponse>());
        }
        return new QueryListResult<TQueryFilterResponse>(
            total,
            totalPages,
            pageNumber,
            pageSize,
            queryFilterResponses.Skip((pageNumber - 1) * pagingParam.PageSize.Value).Take(pageSize));
    }

    public TQueryFilterResponse
        GetSingleProjectionQuery<TAggregate, TSingleProjection, TAggregateProjectionPayload, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param,
            IEnumerable<SingleProjectionState<TAggregateProjectionPayload>> projections) where TAggregate : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregate, TSingleProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleProjectionPayload
        where TQueryFilter : ISingleProjectionQuery<TAggregate, TSingleProjection,
            TAggregateProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var queryFilter = _serviceProvider.GetService<TQueryFilter>();
        if (queryFilter is null) { throw new Exception($"QueryFilter {typeof(TQueryFilter).FullName} is not registered to dependency injection"); }
        var filtered = queryFilter.HandleFilter(param, projections);
        return filtered;
    }
}
