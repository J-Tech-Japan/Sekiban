using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Queries.QueryModels;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class
    BaseSingleAggregateProjectionListQueryFilterController<TAggregate, TSingleAggregateProjection, TQueryFilter, TQueryFilterParameter,
        TQueryFilterResponse> : ControllerBase where TAggregate : AggregateBase, new()
    where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection>, new()
    where TQueryFilter : ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, TSingleAggregateProjection, TQueryFilterParameter,
        TQueryFilterResponse>, new()
    where TQueryFilterParameter : IQueryParameter
{
    protected readonly IQueryFilterService _queryFilterService;
    public BaseSingleAggregateProjectionListQueryFilterController(IQueryFilterService queryFilterService)
    {
        _queryFilterService = queryFilterService;
    }
    [HttpGet]
    [Route("")]
    public async Task<ActionResult<IEnumerable<TQueryFilterResponse>>> GetQueryResult([FromQuery] TQueryFilterParameter queryParam)
    {
        var result = await _queryFilterService
            .GetSingleAggregateProjectionListQueryFilterAsync<TAggregate, TSingleAggregateProjection, TQueryFilter, TQueryFilterParameter,
                TQueryFilterResponse>(queryParam);
        return Ok(result);
    }
}
