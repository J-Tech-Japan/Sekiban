using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultProjections;
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
        GetSingleProjectionListQueryAsync<TAggregatePayload, TSingleProjection, TProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(TQueryParameter param) where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : MultiProjectionBase<TAggregatePayload, TSingleProjection, TProjectionPayload>,
        new()
        where TProjectionPayload : ISingleProjectionPayload
        where TQuery : ISingleProjectionListQuery<TAggregatePayload, TSingleProjection,
            TProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var allProjection = await multiProjectionService
            .GetSingleProjectionList<TAggregatePayload, TSingleProjection, TProjectionPayload>();
        return queryHandler
            .GetSingleProjectionListQuery<TAggregatePayload, TSingleProjection, TProjectionPayload, TQuery,
                TQueryParameter, TQueryResponse>(param, allProjection);
    }
    public async Task<TQueryResponse>
        GetSingleProjectionQueryAsync<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery,
            TQueryParameter, TQueryResponse>(TQueryParameter param) where TAggregatePayload : IAggregatePayload, new()
        where TSingleProjection : MultiProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>,
        new()
        where TSingleProjectionPayload : ISingleProjectionPayload
        where TQuery : ISingleProjectionQuery<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var allProjection = await multiProjectionService
            .GetSingleProjectionList<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>();
        return queryHandler
            .GetSingleProjectionQuery<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery,
                TQueryParameter, TQueryResponse>(param, allProjection);
    }
}
