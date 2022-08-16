using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.WebHelper.Authorizations;
using Sekiban.EventSourcing.WebHelper.Common;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class BaseSingleAggregateProjectionController<TAggregate, TSingleAggregateProjection> : ControllerBase
    where TSingleAggregateProjection : SingleAggregateProjectionBase<TSingleAggregateProjection>, new() where TAggregate : AggregateBase, new()
{
    private readonly SekibanControllerOptions _sekibanControllerOptions;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISingleAggregateService _singleAggregateService;
    public BaseSingleAggregateProjectionController(
        ISingleAggregateService singleAggregateService,
        SekibanControllerOptions sekibanControllerOptions,
        IServiceProvider serviceProvider)
    {
        _singleAggregateService = singleAggregateService;
        _sekibanControllerOptions = sekibanControllerOptions;
        _serviceProvider = serviceProvider;
    }

    [HttpGet]
    [Route("")]
    public virtual async Task<ActionResult<TSingleAggregateProjection>> GetAsync(Guid id, int? toVersion = null)
    {
        if (_sekibanControllerOptions.AuthorizeDefinitionCollection.CheckAuthorization(
                AuthorizeMethodType.SingleAggregateProjection,
                this,
                typeof(TSingleAggregateProjection),
                null,
                null,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied) { return Unauthorized(); }
        return Ok(await _singleAggregateService.GetProjectionAsync<TSingleAggregateProjection>(id, toVersion));
    }
}
