using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.WebHelper.Authorizations;
using Sekiban.EventSourcing.WebHelper.Common;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class BaseSingleAggregateProjectionController<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents> : ControllerBase
    where TAggregate : AggregateBase, new()
    where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>, new
    ()
    where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
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
    public virtual async Task<ActionResult<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>?>> GetAsync(
        Guid id,
        int? toVersion = null)
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
        var result = await _singleAggregateService.GetProjectionAsync<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>(
            id,
            toVersion);
        return Ok(result);
    }
}
