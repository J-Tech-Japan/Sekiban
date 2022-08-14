using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.WebHelper.Authorizations;
using Sekiban.EventSourcing.WebHelper.Common;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class BaseQueryGetController<TAggregate, TAggregateContents> : ControllerBase
    where TAggregate : TransferableAggregateBase<TAggregateContents>, new() where TAggregateContents : IAggregateContents, new()
{
    private readonly SekibanControllerOptions _sekibanControllerOptions;
    private readonly ISingleAggregateService _singleAggregateService;
    public BaseQueryGetController(ISingleAggregateService singleAggregateService, SekibanControllerOptions sekibanControllerOptions)
    {
        _singleAggregateService = singleAggregateService;
        _sekibanControllerOptions = sekibanControllerOptions;
    }

    [HttpGet]
    [Route("")]
    public virtual async Task<ActionResult<AggregateDto<TAggregateContents>>> GetAsync(Guid id, int? toVersion = null)
    {
        if (_sekibanControllerOptions.AuthorizeDefinitionCollection.CheckAuthorization(
                AuthorizeMethodType.Get,
                this,
                typeof(TAggregate),
                null,
                null,
                HttpContext) ==
            AuthorizeResultType.Denied) { return Unauthorized(); }
        return Ok(await _singleAggregateService.GetAggregateDtoAsync<TAggregate, TAggregateContents>(id, toVersion));
    }
}
