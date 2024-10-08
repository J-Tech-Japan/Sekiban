using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Query Handler. Internal use only.
/// </summary>
public class QueryHandler : IQueryContext
{
    private readonly IServiceProvider _serviceProvider;

    public QueryHandler(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;


    public ResultBox<T1> GetRequiredService<T1>() where T1 : class =>
        ResultBox.WrapTry(() => _serviceProvider.GetRequiredService<T1>());

    public ResultBox<TwoValues<T1, T2>> GetRequiredService<T1, T2>() where T1 : class where T2 : class =>
        GetRequiredService<T1>().Combine(ResultBox.WrapTry(() => _serviceProvider.GetRequiredService<T2>()));

    public ResultBox<ThreeValues<T1, T2, T3>> GetRequiredService<T1, T2, T3>()
        where T1 : class where T2 : class where T3 : class =>
        GetRequiredService<T1, T2>().Combine(ResultBox.WrapTry(() => _serviceProvider.GetRequiredService<T3>()));

    public ResultBox<FourValues<T1, T2, T3, T4>> GetRequiredService<T1, T2, T3, T4>()
        where T1 : class where T2 : class where T3 : class where T4 : class =>
        GetRequiredService<T1, T2, T3>().Combine(ResultBox.WrapTry(() => _serviceProvider.GetRequiredService<T4>()));
    public ResultBox<IMultiProjectionService> GetMultiProjectionService() =>
        GetRequiredService<IMultiProjectionService>();

    public ListQueryResult<TQueryResponse>
        GetMultiProjectionListQuery<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param,
            MultiProjectionState<TProjectionPayload> projection)
        where TProjectionPayload : IMultiProjectionPayloadCommon
        where TQuery : IMultiProjectionListQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
        where TQueryResponse : IQueryResponse
    {
        var query = _serviceProvider.GetService<TQuery>() ??
            throw new SekibanQueryNotRegisteredException(
                $"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        var filtered = query.HandleFilter(param, projection);
        var sorted = query.HandleSort(param, filtered);
        var queryResponses = sorted.ToList();
        return param is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
            ? MakeQueryListResult(pagingParam, queryResponses)
            : new ListQueryResult<TQueryResponse>(queryResponses.ToList().Count, null, null, null, queryResponses);
    }

    public Task<ResultBox<ListQueryResult<TQueryResponse>>>
        GetMultiProjectionListQueryNextAsync<TProjectionPayload, TQuery, TQueryResponse>(
            TQuery query,
            MultiProjectionState<TProjectionPayload> projection)
        where TProjectionPayload : IMultiProjectionPayloadCommon
        where TQuery : INextMultiProjectionListQueryAsync<TProjectionPayload, TQuery, TQueryResponse>,
        IEquatable<TQuery>
        where TQueryResponse : notnull =>
        TQuery
            .HandleFilterAsync(projection, query, this)
            .Conveyor(sorted => TQuery.HandleSortAsync(sorted, query, this))
            .Remap(sorted => sorted.ToList())
            .Remap(
                sorted => query is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
                    ? MakeQueryListResult(pagingParam, sorted)
                    : new ListQueryResult<TQueryResponse>(sorted.Count, null, null, null, sorted));

    public ResultBox<ListQueryResult<TQueryResponse>>
        GetMultiProjectionListQueryNext<TProjectionPayload, TQuery, TQueryResponse>(
            TQuery query,
            MultiProjectionState<TProjectionPayload> projection)
        where TProjectionPayload : IMultiProjectionPayloadCommon
        where TQuery : INextMultiProjectionListQuery<TProjectionPayload, TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull =>
        TQuery
            .HandleFilter(projection, query, this)
            .Conveyor(sorted => TQuery.HandleSort(sorted, query, this))
            .Remap(sorted => sorted.ToList())
            .Remap(
                sorted => query is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
                    ? MakeQueryListResult(pagingParam, sorted)
                    : new ListQueryResult<TQueryResponse>(sorted.Count, null, null, null, sorted));


    public TQueryResponse GetMultiProjectionQuery<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        MultiProjectionState<TProjectionPayload> projection) where TProjectionPayload : IMultiProjectionPayloadCommon
        where TQuery : IMultiProjectionQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
        where TQueryResponse : IQueryResponse
    {
        var query = _serviceProvider.GetService<TQuery>() ??
            throw new SekibanQueryNotRegisteredException(
                $"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        var filtered = query.HandleFilter(param, projection);
        return filtered;
    }


    public ResultBox<TQueryResponse> GetMultiProjectionQueryNext<TProjectionPayload, TQuery, TQueryResponse>(
        TQuery query,
        MultiProjectionState<TProjectionPayload> projection) where TProjectionPayload : IMultiProjectionPayloadCommon
        where TQuery : INextMultiProjectionQuery<TProjectionPayload, TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull =>
        TQuery.HandleFilter(projection, query, this);
    public Task<ResultBox<TQueryResponse>> GetMultiProjectionQueryNextAsync<TProjectionPayload, TQuery, TQueryResponse>(
        TQuery query,
        MultiProjectionState<TProjectionPayload> projection) where TProjectionPayload : IMultiProjectionPayloadCommon
        where TQuery : INextMultiProjectionQueryAsync<TProjectionPayload, TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull =>
        TQuery.HandleFilterAsync(projection, query, this);




    public async Task<ListQueryResult<TQueryResponse>>
        GetGeneralListQueryAsync<TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TQuery : IGeneralListQuery<TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
        where TQueryResponse : IQueryResponse
    {
        var query = _serviceProvider.GetService<TQuery>() ??
            throw new SekibanQueryNotRegisteredException(
                $"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        var filtered = await query.HandleFilterAsync(param);
        var sorted = query.HandleSort(param, filtered);
        var queryResponses = sorted.ToList();
        return param is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
            ? MakeQueryListResult(pagingParam, queryResponses)
            : new ListQueryResult<TQueryResponse>(queryResponses.ToList().Count, null, null, null, queryResponses);
    }

    public Task<ResultBox<ListQueryResult<TQueryResponse>>>
        GetGeneralListQueryNextAsync<TQuery, TQueryResponse>(TQuery query)
        where TQuery : INextGeneralListQueryAsync<TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull =>
        TQuery
            .HandleFilterAsync(query, this)
            .Conveyor(sorted => TQuery.HandleSortAsync(sorted, query, this))
            .Remap(sorted => sorted.ToList())
            .Remap(
                sorted => query is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
                    ? MakeQueryListResult(pagingParam, sorted)
                    : new ListQueryResult<TQueryResponse>(sorted.Count, null, null, null, sorted));
    public ResultBox<ListQueryResult<TQueryResponse>> GetGeneralListQueryNext<TQuery, TQueryResponse>(TQuery query)
        where TQuery : INextGeneralListQuery<TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull =>
        TQuery
            .HandleFilter(query, this)
            .Conveyor(sorted => TQuery.HandleSort(sorted, query, this))
            .Remap(sorted => sorted.ToList())
            .Remap(
                sorted => query is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
                    ? MakeQueryListResult(pagingParam, sorted)
                    : new ListQueryResult<TQueryResponse>(sorted.Count, null, null, null, sorted));


    public async Task<TQueryResponse>
        GetGeneralQueryAsync<TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TQuery : IGeneralQuery<TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
        where TQueryResponse : IQueryResponse
    {
        var query = _serviceProvider.GetService<TQuery>() ??
            throw new SekibanQueryNotRegisteredException(
                $"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        var filtered = await query.HandleFilterAsync(param);
        return filtered;
    }

    public Task<ResultBox<TQueryResponse>> GetGeneralQueryNextAsync<TQuery, TQueryResponse>(TQuery query)
        where TQuery : INextGeneralQueryAsync<TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull =>
        TQuery.HandleFilterAsync(query, this);
    public Task<ResultBox<TQueryResponse>> GetGeneralQueryNext<TQuery, TQueryResponse>(TQuery query)
        where TQuery : INextGeneralQuery<TQuery, TQueryResponse>, IEquatable<TQuery> where TQueryResponse : notnull =>
        TQuery.HandleFilter(query, this).ToTask();



    public ListQueryResult<TQueryResponse>
        GetAggregateListQuery<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param,
            IEnumerable<AggregateState<TAggregatePayload>> list) where TAggregatePayload : IAggregatePayloadCommon
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
        where TQueryResponse : IQueryResponse
    {
        var query = _serviceProvider.GetService<TQuery>() ??
            throw new SekibanQueryNotRegisteredException(
                $"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        if (query is not IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse> queryImplement)
        {
            throw new SekibanQueryNotRegisteredException(
                $"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        }
        var filtered = queryImplement.HandleFilter(param, list);
        var sorted = queryImplement.HandleSort(param, filtered);
        var queryResponses = sorted.ToList();
        return param is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
            ? MakeQueryListResult(pagingParam, queryResponses)
            : new ListQueryResult<TQueryResponse>(queryResponses.ToList().Count, null, null, null, queryResponses);
    }




    public async Task<ResultBox<ListQueryResult<TQueryResponse>>>
        GetAggregateListQueryNextAsync<TAggregatePayload, TQuery, TQueryResponse>(
            TQuery query,
            IEnumerable<AggregateState<TAggregatePayload>> list) where TAggregatePayload : IAggregatePayloadCommon
        where TQuery : INextAggregateListQueryAsync<TAggregatePayload, TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull =>
        await TQuery
            .HandleFilterAsync(list, query, this)
            .Conveyor(filtered => TQuery.HandleSortAsync(filtered, query, this))
            .Remap(sorted => sorted.ToList())
            .Remap(
                sorted => query is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
                    ? MakeQueryListResult(pagingParam, sorted)
                    : new ListQueryResult<TQueryResponse>(sorted.Count, null, null, null, sorted));


    public ResultBox<ListQueryResult<TQueryResponse>>
        GetAggregateListQueryNext<TAggregatePayload, TQuery, TQueryResponse>(
            TQuery query,
            IEnumerable<AggregateState<TAggregatePayload>> list) where TAggregatePayload : IAggregatePayloadCommon
        where TQuery : INextAggregateListQuery<TAggregatePayload, TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull =>
        TQuery
            .HandleFilter(list, query, this)
            .Conveyor(filtered => TQuery.HandleSort(filtered, query, this))
            .Remap(sorted => sorted.ToList())
            .Remap(
                sorted => query is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
                    ? MakeQueryListResult(pagingParam, sorted)
                    : new ListQueryResult<TQueryResponse>(sorted.Count, null, null, null, sorted));



    public TQueryResponse GetAggregateQuery<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        IEnumerable<AggregateState<TAggregatePayload>> list) where TAggregatePayload : IAggregatePayloadCommon
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
        where TQueryResponse : IQueryResponse
    {
        var query = _serviceProvider.GetService<TQuery>() ??
            throw new SekibanQueryNotRegisteredException(
                $"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        var filtered = query.HandleFilter(param, list);
        return filtered;
    }

    public ResultBox<TQueryResponse> GetAggregateQueryNext<TAggregatePayload, TQuery, TQueryResponse>(
        TQuery query,
        IEnumerable<AggregateState<TAggregatePayload>> list) where TAggregatePayload : IAggregatePayloadCommon
        where TQuery : INextAggregateQuery<TAggregatePayload, TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull
    {
        var filtered = TQuery.HandleFilter(list, query, this);
        return filtered;
    }
    public async Task<ResultBox<TQueryResponse>> GetAggregateQueryNextAsync<TAggregatePayload, TQuery, TQueryResponse>(
        TQuery query,
        IEnumerable<AggregateState<TAggregatePayload>> list) where TAggregatePayload : IAggregatePayloadCommon
        where TQuery : INextAggregateQueryAsync<TAggregatePayload, TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull
    {
        var filtered = await TQuery.HandleFilterAsync(list, query, this);
        return filtered;
    }


    public ListQueryResult<TQueryResponse>
        GetSingleProjectionListQuery<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param,
            IEnumerable<SingleProjectionState<TSingleProjectionPayload>> projections)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
        where TQueryResponse : IQueryResponse
    {
        var query = _serviceProvider.GetService<TQuery>() ??
            throw new SekibanQueryNotRegisteredException(
                $"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        var filtered = query.HandleFilter(param, projections);
        var sorted = query.HandleSort(param, filtered);
        var queryResponses = sorted.ToList();
        return param is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
            ? MakeQueryListResult(pagingParam, queryResponses)
            : new ListQueryResult<TQueryResponse>(queryResponses.ToList().Count, null, null, null, queryResponses);
    }


    public ResultBox<ListQueryResult<TQueryResponse>>
        GetSingleProjectionListNextQuery<TSingleProjectionPayload, TQuery, TQueryResponse>(
            TQuery query,
            IEnumerable<SingleProjectionState<TSingleProjectionPayload>> projections)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon
        where TQuery : INextSingleProjectionListQuery<TSingleProjectionPayload, TQuery, TQueryResponse>,
        IEquatable<TQuery>
        where TQueryResponse : IQueryResponse
    {
        return TQuery
            .HandleFilter(projections, query, this)
            .Conveyor(sorted => TQuery.HandleSort(sorted, query, this))
            .Remap(sorted => sorted.ToList())
            .Remap(
                sorted => query is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
                    ? MakeQueryListResult(pagingParam, sorted)
                    : new ListQueryResult<TQueryResponse>(sorted.Count, null, null, null, sorted));
    }
    public Task<ResultBox<ListQueryResult<TQueryResponse>>>
        GetSingleProjectionListNextQueryAsync<TSingleProjectionPayload, TQuery, TQueryResponse>(
            TQuery query,
            IEnumerable<SingleProjectionState<TSingleProjectionPayload>> projections)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon
        where TQuery : INextSingleProjectionListQueryAsync<TSingleProjectionPayload, TQuery, TQueryResponse>,
        IEquatable<TQuery>
        where TQueryResponse : IQueryResponse
    {
        return TQuery
            .HandleFilterAsync(projections, query, this)
            .Conveyor(sorted => TQuery.HandleSortAsync(sorted, query, this))
            .Remap(sorted => sorted.ToList())
            .Remap(
                sorted => query is IQueryPagingParameterCommon { PageNumber: not null, PageSize: not null } pagingParam
                    ? MakeQueryListResult(pagingParam, sorted)
                    : new ListQueryResult<TQueryResponse>(sorted.Count, null, null, null, sorted));
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
        IEnumerable<SingleProjectionState<TSingleProjectionPayload>> projections)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
        where TQueryResponse : IQueryResponse
    {
        var query = _serviceProvider.GetService<TQuery>() ??
            throw new SekibanQueryNotRegisteredException(
                $"AddQuery {typeof(TQuery).FullName} is not registered to dependency injection");
        var filtered = query.HandleFilter(param, projections);
        return filtered;
    }
    public Task<ResultBox<TQueryResponse>>
        GetSingleProjectionQueryNextAsync<TSingleProjectionPayload, TQuery, TQueryResponse>(
            TQuery query,
            IEnumerable<SingleProjectionState<TSingleProjectionPayload>> projections)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon
        where TQuery : INextSingleProjectionQueryAsync<TSingleProjectionPayload, TQuery, TQueryResponse>,
        IEquatable<TQuery>
        where TQueryResponse : notnull =>
        TQuery.HandleFilterAsync(projections, query, this);
    public ResultBox<TQueryResponse> GetSingleProjectionQueryNext<TSingleProjectionPayload, TQuery, TQueryResponse>(
        TQuery query,
        IEnumerable<SingleProjectionState<TSingleProjectionPayload>> projections)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon
        where TQuery : INextSingleProjectionQuery<TSingleProjectionPayload, TQuery, TQueryResponse>, IEquatable<TQuery>
        where TQueryResponse : notnull =>
        TQuery.HandleFilter(projections, query, this);
}
