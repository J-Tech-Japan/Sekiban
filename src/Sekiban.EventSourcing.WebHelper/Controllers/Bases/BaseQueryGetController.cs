using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

[ApiController]
public class BaseQueryGetController<TAggregate, TAggregateContents> : ControllerBase
    where TAggregate : TransferableAggregateBase<TAggregateContents>, new() where TAggregateContents : IAggregateContents, new()
{
    private readonly ISingleAggregateService _singleAggregateService;
    public BaseQueryGetController(ISingleAggregateService singleAggregateService) =>
        _singleAggregateService = singleAggregateService;

    [HttpGet]
    [Route("")]
    public virtual async Task<ActionResult<AggregateDto<TAggregateContents>>> GetAsync(Guid id, int? toVersion = null) =>
        Ok(await _singleAggregateService.GetAggregateDtoAsync<TAggregate, TAggregateContents>(id, toVersion));
}
