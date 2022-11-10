using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public class QueryService : IQueryService
{
    private readonly IMultiProjectionService multiProjectionService;
    private readonly QueryHandler queryHandler;
    public QueryService(IMultiProjectionService multiProjectionService, QueryHandler queryHandler)
    {
        this.multiProjectionService = multiProjectionService;
        this.queryHandler = queryHandler;
    }
    public async Task<TQueryResponse>
        GetMultiProjectionQueryAsync<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param) where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionQuery<TProjection, TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var allProjection = await multiProjectionService.GetMultiProjectionAsync<TProjection, TProjectionPayload>();
        return queryHandler
            .GetMultiProjectionQuery<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param,
                allProjection);
    }
    public async Task<ListQueryResult<TQueryResponse>>
        GetMultiProjectionListQueryAsync<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param) where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQuery : IMultiProjectionListQuery<TProjection, TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var allProjection = await multiProjectionService.GetMultiProjectionAsync<TProjection, TProjectionPayload>();
        return queryHandler
            .GetMultiProjectionListQuery<TProjection, TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param,
                allProjection);
    }
    public async Task<ListQueryResult<TQueryResponse>>
        GetAggregateListQueryAsync<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
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
        GetAggregateQueryAsync<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(TQueryParameter param)
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
        GetSingleProjectionListQueryAsync<TProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TProjectionPayload : ISingleProjectionPayload, new()
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
        GetSingleProjectionQueryAsync<TSingleProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(TQueryParameter param)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
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
