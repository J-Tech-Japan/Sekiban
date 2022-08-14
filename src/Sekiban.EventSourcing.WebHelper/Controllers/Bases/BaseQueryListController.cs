using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.WebHelper.Authorizations;
using Sekiban.EventSourcing.WebHelper.Common;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

[Produces("application/json")]
public class BaseQueryListController<TAggregate, TAggregateContents> : ControllerBase
    where TAggregate : TransferableAggregateBase<TAggregateContents>, new() where TAggregateContents : IAggregateContents, new()
{
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    private readonly SekibanControllerOptions _sekibanControllerOptions;
    private readonly IServiceProvider _serviceProvider;
    public BaseQueryListController(
        IMultipleAggregateProjectionService multipleAggregateProjectionService,
        SekibanControllerOptions sekibanControllerOptions,
        IServiceProvider serviceProvider)
    {
        _multipleAggregateProjectionService = multipleAggregateProjectionService;
        _sekibanControllerOptions = sekibanControllerOptions;
        _serviceProvider = serviceProvider;
    }

    [HttpGet]
    [Route("")]
    public virtual async Task<ActionResult<IEnumerable<AggregateDto<TAggregateContents>>>> ListAsync()
    {
        if (_sekibanControllerOptions.AuthorizeDefinitionCollection.CheckAuthorization(
                AuthorizeMethodType.List,
                this,
                typeof(TAggregate),
                null,
                null,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied) { return Unauthorized(); }

        return Ok(await _multipleAggregateProjectionService.GetAggregateList<TAggregate, TAggregateContents>());
    }
}
