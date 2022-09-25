using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Queries.QueryModels;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class BaseAggregateListQueryFilterController<TAggregate, TAggregateContents, TQueryFilter, TQueryParam, TResponseQueryModel> : ControllerBase
    where TAggregate : TransferableAggregateBase<TAggregateContents>
    where TAggregateContents : IAggregateContents, new()
    where TQueryFilter : IAggregateListQueryFilterDefinition<TAggregate, TAggregateContents, TQueryParam, TResponseQueryModel>, new()
    where TQueryParam : IQueryParameter
{
    protected readonly IQueryFilterService _queryFilterService;
    public BaseAggregateListQueryFilterController(IQueryFilterService queryFilterService)
    {
        _queryFilterService = queryFilterService;
    }
    [HttpGet]
    [Route("")]
    public async Task<ActionResult<IEnumerable<TResponseQueryModel>>> GetQueryResult([FromQuery] TQueryParam queryParam)
    {
        var result = await _queryFilterService
            .GetAggregateListQueryFilterAsync<TAggregate, TAggregateContents, TQueryFilter, TQueryParam, TResponseQueryModel>(queryParam);
        return Ok(result);
    }
}
