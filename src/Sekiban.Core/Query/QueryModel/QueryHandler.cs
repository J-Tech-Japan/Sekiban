using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public class QueryHandler
{
    private readonly IServiceProvider _serviceProvider;

    public QueryHandler(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    public ListQueryResult<TQueryResponse>
        GetMultiProjectionListQuery<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param,
            MultiProjectionState<TProjectionPayload> projection)
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionListQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
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
        return new ListQueryResult<TQueryResponse>(queryResponses.ToList().Count, null, null, null, queryResponses);
    }

    public TQueryResponse GetMultiProjectionQuery<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        MultiProjectionState<TProjectionPayload> projection)
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var query = _serviceProvider.GetService<TQuery>();
        if (query is null) { throw new Exception($"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection"); }
        var filtered = query.HandleFilter(param, projection);
        return filtered;
    }


    public ListQueryResult<TQueryResponse>
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
        return new ListQueryResult<TQueryResponse>(queryResponses.ToList().Count, null, null, null, queryResponses);
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

    public ListQueryResult<TQueryResponse>
        GetSingleProjectionListQuery<TSingleProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(
            TQueryParameter param,
            IEnumerable<SingleProjectionState<TSingleProjectionPayload>> projections)
        where TSingleProjectionPayload : ISingleProjectionPayload
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
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
        return new ListQueryResult<TQueryResponse>(queryResponses.ToList().Count, null, null, null, queryResponses);
    }

    private static ListQueryResult<TQueryResponse> makeQueryListResult<TQueryResponse>(
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
            return new ListQueryResult<TQueryResponse>(total, totalPages, pageNumber, pageSize, new List<TQueryResponse>());
        }
        return new ListQueryResult<TQueryResponse>(
            total,
            totalPages,
            pageNumber,
            pageSize,
            queryResponses.Skip((pageNumber - 1) * pagingParam.PageSize.Value).Take(pageSize));
    }

    public TQueryResponse
        GetSingleProjectionQuery<TSingleProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(
            TQueryParameter param,
            IEnumerable<SingleProjectionState<TSingleProjectionPayload>> projections)
        where TSingleProjectionPayload : ISingleProjectionPayload
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var query = _serviceProvider.GetService<TQuery>();
        if (query is null) { throw new Exception($"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection"); }
        var filtered = query.HandleFilter(param, projections);
        return filtered;
    }
}
