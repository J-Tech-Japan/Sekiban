using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.WebHelper.Authorizations;
using Sekiban.EventSourcing.WebHelper.Common;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class BaseAggregateQueryController<TAggregate, TAggregateContents> : ControllerBase
    where TAggregate : TransferableAggregateBase<TAggregateContents>, new() where TAggregateContents : IAggregateContents, new()
{
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    private readonly SekibanControllerOptions _sekibanControllerOptions;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISingleAggregateService _singleAggregateService;
    public BaseAggregateQueryController(
        ISingleAggregateService singleAggregateService,
        SekibanControllerOptions sekibanControllerOptions,
        IServiceProvider serviceProvider,
        IMultipleAggregateProjectionService multipleAggregateProjectionService)
    {
        _singleAggregateService = singleAggregateService;
        _sekibanControllerOptions = sekibanControllerOptions;
        _serviceProvider = serviceProvider;
        _multipleAggregateProjectionService = multipleAggregateProjectionService;
    }

    [HttpGet]
    [Route("get/{id}")]
    public virtual async Task<ActionResult<AggregateDto<TAggregateContents>>> GetAsync(Guid id, int? toVersion = null)
    {
        if (_sekibanControllerOptions.AuthorizeDefinitionCollection.CheckAuthorization(
                AuthorizeMethodType.Get,
                this,
                typeof(TAggregate),
                null,
                null,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied) { return Unauthorized(); }
        return Ok(await _singleAggregateService.GetAggregateDtoAsync<TAggregate, TAggregateContents>(id, toVersion));
    }
    [HttpGet]
    [Route("getids")]
    public virtual async Task<ActionResult<IEnumerable<AggregateDto<TAggregateContents>>>> GetIdsAsync([FromQuery] IEnumerable<Guid> ids)
    {
        await Task.CompletedTask;
        if (_sekibanControllerOptions.AuthorizeDefinitionCollection.CheckAuthorization(
                AuthorizeMethodType.Get,
                this,
                typeof(TAggregate),
                null,
                null,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied) { return Unauthorized(); }
        var result = ids.Select(id => _singleAggregateService.GetAggregateDtoAsync<TAggregate, TAggregateContents>(id));
        return Ok(result);
    }
    [HttpGet]
    [Route("list")]
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
