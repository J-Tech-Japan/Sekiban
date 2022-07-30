using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

public class BaseQueryListController<TAggregate, TAggregateContents> : ControllerBase
    where TAggregate : TransferableAggregateBase<TAggregateContents>, new() where TAggregateContents : IAggregateContents, new()
{
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    public BaseQueryListController(IMultipleAggregateProjectionService multipleAggregateProjectionService) =>
        _multipleAggregateProjectionService = multipleAggregateProjectionService;

    [HttpGet]
    [Route("")]
    public async Task<ActionResult<IEnumerable<AggregateDto<TAggregateContents>>>> ListAsync() =>
        Ok(await _multipleAggregateProjectionService.GetAggregateList<TAggregate, TAggregateContents>());
}
