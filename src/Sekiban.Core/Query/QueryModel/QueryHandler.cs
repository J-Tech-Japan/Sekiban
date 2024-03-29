using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Query Handler. Internal use only.
/// </summary>
public class QueryHandler
{
    private readonly IServiceProvider _serviceProvider;

    public QueryHandler(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    public ListQueryResult<TQueryResponse> GetMultiProjectionListQuery<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        MultiProjectionState<TProjectionPayload> projection) where TProjectionPayload : IMultiProjectionPayloadCommon
        where TQuery : IMultiProjectionListQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var query = _serviceProvider.GetService<TQuery>() ??
            throw new SekibanQueryNotRegisteredException($"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        var filtered = query.HandleFilter(param, projection);
        var sorted = query.HandleSort(param, filtered);
        var queryResponses = sorted.ToList();
        return param is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
            ? MakeQueryListResult(pagingParam, queryResponses)
            : new ListQueryResult<TQueryResponse>(queryResponses.ToList().Count, null, null, null, queryResponses);
    }

    public TQueryResponse GetMultiProjectionQuery<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        MultiProjectionState<TProjectionPayload> projection) where TProjectionPayload : IMultiProjectionPayloadCommon
        where TQuery : IMultiProjectionQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var query = _serviceProvider.GetService<TQuery>() ??
            throw new SekibanQueryNotRegisteredException($"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        var filtered = query.HandleFilter(param, projection);
        return filtered;
    }


    public async Task<ListQueryResult<TQueryResponse>> GetGeneralListQueryAsync<TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TQuery : IGeneralListQuery<TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var query = _serviceProvider.GetService<TQuery>() ??
            throw new SekibanQueryNotRegisteredException($"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        var filtered = await query.HandleFilterAsync(param);
        var sorted = query.HandleSort(param, filtered);
        var queryResponses = sorted.ToList();
        return param is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
            ? MakeQueryListResult(pagingParam, queryResponses)
            : new ListQueryResult<TQueryResponse>(queryResponses.ToList().Count, null, null, null, queryResponses);
    }

    public async Task<TQueryResponse> GetGeneralQueryAsync<TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TQuery : IGeneralQuery<TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var query = _serviceProvider.GetService<TQuery>() ??
            throw new SekibanQueryNotRegisteredException($"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        var filtered = await query.HandleFilterAsync(param);
        return filtered;
    }


    public ListQueryResult<TQueryResponse> GetAggregateListQuery<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        IEnumerable<AggregateState<TAggregatePayload>> list) where TAggregatePayload : IAggregatePayloadCommon
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var query = _serviceProvider.GetService<TQuery>() ??
            throw new SekibanQueryNotRegisteredException($"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        if (query is not IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse> queryImplement)
        {
            throw new SekibanQueryNotRegisteredException($"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        }
        var filtered = queryImplement.HandleFilter(param, list);
        var sorted = queryImplement.HandleSort(param, filtered);
        var queryResponses = sorted.ToList();
        return param is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
            ? MakeQueryListResult(pagingParam, queryResponses)
            : new ListQueryResult<TQueryResponse>(queryResponses.ToList().Count, null, null, null, queryResponses);
    }

    public TQueryResponse GetAggregateQuery<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        IEnumerable<AggregateState<TAggregatePayload>> list) where TAggregatePayload : IAggregatePayloadCommon
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var query = _serviceProvider.GetService<TQuery>() ??
            throw new SekibanQueryNotRegisteredException($"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        var filtered = query.HandleFilter(param, list);
        return filtered;
    }

    public ListQueryResult<TQueryResponse> GetSingleProjectionListQuery<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        IEnumerable<SingleProjectionState<TSingleProjectionPayload>> projections) where TSingleProjectionPayload : ISingleProjectionPayloadCommon
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var query = _serviceProvider.GetService<TQuery>() ??
            throw new SekibanQueryNotRegisteredException($"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        var filtered = query.HandleFilter(param, projections);
        var sorted = query.HandleSort(param, filtered);
        var queryResponses = sorted.ToList();
        return param is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
            ? MakeQueryListResult(pagingParam, queryResponses)
            : new ListQueryResult<TQueryResponse>(queryResponses.ToList().Count, null, null, null, queryResponses);
    }

    private static ListQueryResult<TQueryResponse> MakeQueryListResult<TQueryResponse>(
        IQueryPagingParameterCommon pagingParam,
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
        return pageNumber < 1 || pageNumber > totalPages
            ? new ListQueryResult<TQueryResponse>(total, totalPages, pageNumber, pageSize, new List<TQueryResponse>())
            : new ListQueryResult<TQueryResponse>(
                total,
                totalPages,
                pageNumber,
                pageSize,
                queryResponses.Skip((pageNumber - 1) * pagingParam.PageSize.Value).Take(pageSize));
    }

    public TQueryResponse GetSingleProjectionQuery<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        IEnumerable<SingleProjectionState<TSingleProjectionPayload>> projections) where TSingleProjectionPayload : ISingleProjectionPayloadCommon
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
    {
        var query = _serviceProvider.GetService<TQuery>() ??
            throw new SekibanQueryNotRegisteredException($"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        var filtered = query.HandleFilter(param, projections);
        return filtered;
    }
}
