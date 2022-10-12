using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Queries.QueryModels;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class
    BaseAggregateListQueryFilterController<TAggregate, TAggregateContents, TQueryFilter, TQueryParameter, TQueryFilterResponse> : ControllerBase
    where TAggregate : TransferableAggregateBase<TAggregateContents>
    where TAggregateContents : IAggregateContents, new()
    where TQueryFilter : IAggregateListQueryFilterDefinition<TAggregate, TAggregateContents, TQueryParameter, TQueryFilterResponse>, new()
    where TQueryParameter : IQueryParameter
{
    protected readonly IQueryFilterService _queryFilterService;
    public BaseAggregateListQueryFilterController(IQueryFilterService queryFilterService)
    {
        _queryFilterService = queryFilterService;
    }
    [HttpGet]
    [Route("")]
    public async Task<ActionResult<QueryFilterListResult<TQueryFilterResponse>>> GetQueryResult([FromQuery] TQueryParameter queryParam)
    {
        var result = await _queryFilterService
            .GetAggregateListQueryFilterAsync<TAggregate, TAggregateContents, TQueryFilter, TQueryParameter, TQueryFilterResponse>(queryParam);
        return Ok(result);
    }
}
