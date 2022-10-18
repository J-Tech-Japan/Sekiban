using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Queries.QueryModels;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class BaseSingleAggregateProjectionQueryFilterController<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents,
    TQueryFilter, TQueryFilterParameter, TQueryFilterResponse> : ControllerBase where TAggregate : AggregateCommonBase, new()
    where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>, new
    ()
    where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
    where TQueryFilter : ISingleAggregateProjectionQueryFilterDefinition<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents,
        TQueryFilterParameter, TQueryFilterResponse>, new()
    where TQueryFilterParameter : IQueryParameter
{
    protected readonly IQueryFilterService _queryFilterService;
    public BaseSingleAggregateProjectionQueryFilterController(IQueryFilterService queryFilterService)
    {
        _queryFilterService = queryFilterService;
    }
    [HttpGet]
    [Route("")]
    public async Task<ActionResult<TQueryFilterResponse>> GetQueryResult([FromQuery] TQueryFilterParameter queryParam)
    {
        var result = await _queryFilterService
            .GetSingleAggregateProjectionQueryFilterAsync<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents, TQueryFilter,
                TQueryFilterParameter, TQueryFilterResponse>(queryParam);
        return Ok(result);
    }
}
