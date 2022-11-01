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

    public QueryListResult<TQueryResponse>
        GetMultiProjectionListQuery<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param,
            MultiProjectionState<TProjectionPayload> projection)
        where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionListQuery<TProjection, TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var query = _serviceProvider.GetService<TQuery>();
        if (query is null) { throw new Exception($"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection"); }
        var filtered = query.HandleFilter(param, projection);
        var sorted = query.HandleSort(param, filtered);
        var queryResponses = sorted.ToList();
        if (param is IQueryPagingParameter { PageNumber: { }, PageSize: { } } pagingParam)
        {
            return makeQueryListResult(pagingParam, queryResponses);
        }
        return new QueryListResult<TQueryResponse>(queryResponses.ToList().Count, null, null, null, queryResponses);
    }

    public TQueryResponse GetMultiProjectionQuery<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        MultiProjectionState<TProjectionPayload> projection)
        where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionQuery<TProjection, TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var query = _serviceProvider.GetService<TQuery>();
        if (query is null) { throw new Exception($"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection"); }
        var filtered = query.HandleFilter(param, projection);
        return filtered;
    }


    public QueryListResult<TQueryResponse>
        GetAggregateListQuery<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param,
            IEnumerable<AggregateState<TAggregatePayload>> list)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var query = _serviceProvider.GetService<TQuery>();
        if (query is null) { throw new Exception($"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection"); }
        var filtered = query.HandleFilter(param, list);
        var sorted = query.HandleSort(param, filtered);
        var queryResponses = sorted.ToList();
        if (param is IQueryPagingParameter { PageNumber: { }, PageSize: { } } pagingParam)
        {
            return makeQueryListResult(pagingParam, queryResponses);
        }
        return new QueryListResult<TQueryResponse>(queryResponses.ToList().Count, null, null, null, queryResponses);
    }
    public TQueryResponse GetAggregateQuery<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        IEnumerable<AggregateState<TAggregatePayload>> list)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var query = _serviceProvider.GetService<TQuery>();
        if (query is null) { throw new Exception($"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection"); }
        var filtered = query.HandleFilter(param, list);
        return filtered;
    }

    public QueryListResult<TQueryResponse>
        GetSingleProjectionListQuery<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(
            TQueryParameter param,
            IEnumerable<SingleProjectionState<TSingleProjectionPayload>> projections) where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>,
        new()
        where TSingleProjectionPayload : ISingleProjectionPayload
        where TQuery : ISingleProjectionListQuery<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var query = _serviceProvider.GetService<TQuery>();
        if (query is null) { throw new Exception($"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection"); }
        var filtered = query.HandleFilter(param, projections);
        var sorted = query.HandleSort(param, filtered);
        var queryResponses = sorted.ToList();
        if (param is IQueryPagingParameter { PageNumber: { }, PageSize: { } } pagingParam)
        {
            return makeQueryListResult(pagingParam, queryResponses);
        }
        return new QueryListResult<TQueryResponse>(queryResponses.ToList().Count, null, null, null, queryResponses);
    }

    private static QueryListResult<TQueryResponse> makeQueryListResult<TQueryResponse>(
        IQueryPagingParameter pagingParam,
        List<TQueryResponse> queryResponses)
    {
        if (pagingParam.PageNumber == null || pagingParam.PageSize == null)
        {
            throw new SekibanQueryPagingError();
        }
        var pageNumber = pagingParam.PageNumber.Value;
        var pageSize = pagingParam.PageSize.Value;
        var total = queryResponses.ToList().Count;
        var totalPages = total / pagingParam.PageSize.Value + (total % pagingParam.PageSize.Value > 0 ? 1 : 0);
        if (pageNumber < 1 || pageNumber > totalPages)
        {
            return new QueryListResult<TQueryResponse>(total, totalPages, pageNumber, pageSize, new List<TQueryResponse>());
        }
        return new QueryListResult<TQueryResponse>(
            total,
            totalPages,
            pageNumber,
            pageSize,
            queryResponses.Skip((pageNumber - 1) * pagingParam.PageSize.Value).Take(pageSize));
    }

    public TQueryResponse
        GetSingleProjectionQuery<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(
            TQueryParameter param,
            IEnumerable<SingleProjectionState<TSingleProjectionPayload>> projections) where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>,
        new()
        where TSingleProjectionPayload : ISingleProjectionPayload
        where TQuery : ISingleProjectionQuery<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var query = _serviceProvider.GetService<TQuery>();
        if (query is null) { throw new Exception($"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection"); }
        var filtered = query.HandleFilter(param, projections);
        return filtered;
    }
}
