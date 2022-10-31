using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultipleProjections;
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
    public async Task<TQueryFilterResponse>
        GetMultiProjectionQueryFilterAsync<TProjection, TProjectionPayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param) where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQueryFilter : IMultiProjectionQuery<TProjection, TProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await multiProjectionService.GetMultiProjectionAsync<TProjection, TProjectionPayload>();
        return queryHandler
            .GetMultiProjectionQuery<TProjection, TProjectionPayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
                param,
                allProjection);
    }
    public async Task<QueryListResult<TQueryFilterResponse>>
        GetMultiProjectionListQueryAsync<TProjection, TProjectionPayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param) where TProjection : MultiProjectionBase<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
        where TQueryFilter : IMultiProjectionListQuery<TProjection, TProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await multiProjectionService.GetMultiProjectionAsync<TProjection, TProjectionPayload>();
        return queryHandler
            .GetMultiProjectionListQuery<TProjection, TProjectionPayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
                param,
                allProjection);
    }
    public async Task<QueryListResult<TQueryFilterResponse>>
        GetAggregateListQueryAsync<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQueryFilter : IAggregateListQuery<TAggregatePayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await multiProjectionService.GetAggregateList<TAggregatePayload>();
        return queryHandler
            .GetAggregateListQuery<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
                param,
                allProjection);
    }
    public async Task<TQueryFilterResponse>
        GetAggregateQueryAsync<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param)
        where TAggregatePayload : IAggregatePayload, new()
        where TQueryFilter : IAggregateQuery<TAggregatePayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await multiProjectionService.GetAggregateList<TAggregatePayload>();
        return queryHandler.GetAggregateQuery<TAggregatePayload, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            param,
            allProjection);
    }
    public async Task<QueryListResult<TQueryFilterResponse>>
        GetSingleProjectionListQueryAsync<TAggregate, TSingleProjection, TProjectionPayload, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregate, TSingleProjection, TProjectionPayload>,
        new()
        where TProjectionPayload : ISingleProjectionPayload
        where TQueryFilter : ISingleProjectionListQuery<TAggregate, TSingleProjection,
            TProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await multiProjectionService
            .GetSingleProjectionList<TAggregate, TSingleProjection, TProjectionPayload>();
        return queryHandler
            .GetSingleProjectionListQuery<TAggregate, TSingleProjection, TProjectionPayload, TQueryFilter,
                TQueryFilterParameter, TQueryFilterResponse>(param, allProjection);
    }
    public async Task<TQueryFilterResponse>
        GetSingleProjectionQueryAsync<TAggregate, TSingleProjection, TAggregateProjectionPayload, TQueryFilter,
            TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : IAggregatePayload, new()
        where TSingleProjection : SingleProjectionBase<TAggregate, TSingleProjection, TAggregateProjectionPayload>,
        new()
        where TAggregateProjectionPayload : ISingleProjectionPayload
        where TQueryFilter : ISingleProjectionQuery<TAggregate, TSingleProjection,
            TAggregateProjectionPayload, TQueryFilterParameter, TQueryFilterResponse>
        where TQueryFilterParameter : IQueryParameter
    {
        var allProjection = await multiProjectionService
            .GetSingleProjectionList<TAggregate, TSingleProjection, TAggregateProjectionPayload>();
        return queryHandler
            .GetSingleProjectionQuery<TAggregate, TSingleProjection, TAggregateProjectionPayload, TQueryFilter,
                TQueryFilterParameter, TQueryFilterResponse>(param, allProjection);
    }
}
