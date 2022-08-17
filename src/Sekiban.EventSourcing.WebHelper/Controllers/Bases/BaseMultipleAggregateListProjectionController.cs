using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.WebHelper.Authorizations;
using Sekiban.EventSourcing.WebHelper.Common;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

[Produces("application/json")]
public class BaseMultipleAggregateListProjectionController<TProjection, TRecord> : ControllerBase
    where TProjection : MultipleAggregateListProjectionBase<TProjection, TRecord>, IMultipleAggregateProjectionDto, new() where TRecord : new()
{
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    private readonly SekibanControllerOptions _sekibanControllerOptions;
    private readonly IServiceProvider _serviceProvider;
    public BaseMultipleAggregateListProjectionController(
        IMultipleAggregateProjectionService multipleAggregateProjectionService,
        SekibanControllerOptions sekibanControllerOptions,
        IServiceProvider serviceProvider)
    {
        _multipleAggregateProjectionService = multipleAggregateProjectionService;
        _sekibanControllerOptions = sekibanControllerOptions;
        _serviceProvider = serviceProvider;
    }

    [HttpGet]
    [Route("get")]
    public virtual async Task<ActionResult<TProjection>> GetAsync()
    {
        if (_sekibanControllerOptions.AuthorizeDefinitionCollection.CheckAuthorization(
                AuthorizeMethodType.MultipleAggregateListProjection,
                this,
                typeof(TProjection),
                null,
                null,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied) { return Unauthorized(); }

        return Ok(await _multipleAggregateProjectionService.GetListProjectionAsync<TProjection, TRecord>());
    }

    [HttpGet]
    [Route("list")]
    public virtual async Task<ActionResult<IEnumerable<TRecord>>> ListAsync()
    {
        if (_sekibanControllerOptions.AuthorizeDefinitionCollection.CheckAuthorization(
                AuthorizeMethodType.MultipleAggregateListProjection,
                this,
                typeof(TProjection),
                null,
                null,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied) { return Unauthorized(); }
        var result = await _multipleAggregateProjectionService.GetListProjectionAsync<TProjection, TRecord>();
        return Ok(result.Records);
    }
}
