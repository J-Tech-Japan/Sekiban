using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.WebHelper.Authorizations;
using Sekiban.EventSourcing.WebHelper.Common;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

public class BaseMultipleAggregateListOnlyProjectionController<TProjection, TRecord> : ControllerBase
    where TProjection : MultipleAggregateListProjectionBase<TProjection, TRecord>, IMultipleAggregateProjectionDto, new() where TRecord : new()
{
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    private readonly SekibanControllerOptions _sekibanControllerOptions;
    private readonly IServiceProvider _serviceProvider;
    public BaseMultipleAggregateListOnlyProjectionController(
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
    public virtual async Task<ActionResult<IEnumerable<TRecord>>> GetMultipleProjectionAsync()
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
