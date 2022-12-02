using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;

namespace Sekiban.Core.Query.QueryModel;

public class QueryExecutor : IQueryExecutor
{
    private readonly IMultiProjectionService multiProjectionService;
    private readonly QueryHandler queryHandler;

    public QueryExecutor(IMultiProjectionService multiProjectionService, QueryHandler queryHandler)
    {
        this.multiProjectionService = multiProjectionService;
        this.queryHandler = queryHandler;
    }

    public async Task<TQueryResponse>
        ForMultiProjectionQueryAsync<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param)
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var allProjection = await multiProjectionService.GetMultiProjectionAsync<TProjectionPayload>();
        return queryHandler
            .GetMultiProjectionQuery<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param,
                allProjection);
    }

    public async Task<ListQueryResult<TQueryResponse>>
        ForMultiProjectionListQueryAsync<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param)
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
        where TQuery : IMultiProjectionListQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var allProjection = await multiProjectionService.GetMultiProjectionAsync<TProjectionPayload>();
        return queryHandler
            .GetMultiProjectionListQuery<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param,
                allProjection);
    }

    public async Task<ListQueryResult<TQueryResponse>>
        ForAggregateListQueryAsync<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var allProjection = await multiProjectionService.GetAggregateList<TAggregatePayload>();
        return queryHandler
            .GetAggregateListQuery<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
                param,
                allProjection);
    }

    public async Task<TQueryResponse>
        ForAggregateQueryAsync<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var allProjection = await multiProjectionService.GetAggregateList<TAggregatePayload>();
        return queryHandler.GetAggregateQuery<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            allProjection);
    }

    public async Task<ListQueryResult<TQueryResponse>>
        ForSingleProjectionListQueryAsync<TProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionListQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var allProjection = await multiProjectionService
            .GetSingleProjectionList<TProjectionPayload>();
        return queryHandler
            .GetSingleProjectionListQuery<TProjectionPayload, TQuery,
                TQueryParameter, TQueryResponse>(param, allProjection);
    }

    public async Task<TQueryResponse>
        ForSingleProjectionQueryAsync<TSingleProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var allProjection = await multiProjectionService
            .GetSingleProjectionList<TSingleProjectionPayload>();
        return queryHandler
            .GetSingleProjectionQuery<TSingleProjectionPayload, TQuery,
                TQueryParameter, TQueryResponse>(param, allProjection);
    }
}
