using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.WebHelper.Controllers.Bases;

[ApiController]
[ApiExplorerSettings(IgnoreApi = false)]
public class BaseQueryController<TAggregate, TAggregateContents> : ControllerBase
    where TAggregate : TransferableAggregateBase<TAggregateContents>, new() where TAggregateContents : IAggregateContents, new()
{
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    private readonly ISingleAggregateService _singleAggregateService;
    public BaseQueryController(IMultipleAggregateProjectionService multipleAggregateProjectionService, ISingleAggregateService singleAggregateService)
    {
        _multipleAggregateProjectionService = multipleAggregateProjectionService;
        _singleAggregateService = singleAggregateService;
    }

    [HttpGet]
    [Route("{aggregateId}")]
    public async Task<ActionResult<AggregateDto<TAggregateContents>>> GetAsync(Guid aggregateId, int? toVersion = null) =>
        Ok(await _singleAggregateService.GetAggregateAsync<TAggregate, TAggregateContents>(aggregateId, toVersion));
    [HttpGet]
    [Route("list")]
    public async Task<ActionResult<IEnumerable<AggregateDto<TAggregateContents>>>> ListAsync() =>
        Ok(await _multipleAggregateProjectionService.GetAggregateList<TAggregate, TAggregateContents>());
}
