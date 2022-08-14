using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.WebHelper.Authorizations;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

[Produces("application/json")]
public class BaseQueryListController<TAggregate, TAggregateContents> : ControllerBase
    where TAggregate : TransferableAggregateBase<TAggregateContents>, new() where TAggregateContents : IAggregateContents, new()
{
    private readonly IAuthorizeDefinitionCollection _authorizeDefinitionCollection;
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    public BaseQueryListController(
        IMultipleAggregateProjectionService multipleAggregateProjectionService,
        IAuthorizeDefinitionCollection authorizeDefinitionCollection)
    {
        _multipleAggregateProjectionService = multipleAggregateProjectionService;
        _authorizeDefinitionCollection = authorizeDefinitionCollection;
    }

    [HttpGet]
    [Route("")]
    public virtual async Task<ActionResult<IEnumerable<AggregateDto<TAggregateContents>>>> ListAsync()
    {
        if (_authorizeDefinitionCollection.CheckAuthorization(
                AuthorizeMethodType.List,
                this,
                typeof(TAggregate),
                null,
                null,
                HttpContext) ==
            AuthorizeResultType.Denied) { return Unauthorized(); }

        return Ok(await _multipleAggregateProjectionService.GetAggregateList<TAggregate, TAggregateContents>());
    }
}
