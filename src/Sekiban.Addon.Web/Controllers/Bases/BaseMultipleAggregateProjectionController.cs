using Microsoft.AspNetCore.Mvc;
using Sekiban.Addon.Web.Authorizations;
using Sekiban.Addon.Web.Common;
using Sekiban.Core.Query.MultipleAggregate;
namespace Sekiban.Addon.Web.Controllers.Bases;

[Produces("application/json")]
public class BaseMultipleAggregateProjectionController<TProjection, TProjectionContents> : ControllerBase
    where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
    where TProjectionContents : IMultipleAggregateProjectionContents, new()
{
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    private readonly SekibanControllerOptions _sekibanControllerOptions;
    private readonly IServiceProvider _serviceProvider;
    public BaseMultipleAggregateProjectionController(
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
    public virtual async Task<ActionResult<MultipleAggregateProjectionContentsDto<TProjectionContents>>> GetMultipleProjectionAsync()
    {
        if (_sekibanControllerOptions.AuthorizeDefinitionCollection.CheckAuthorization(
                AuthorizeMethodType.MultipleAggregateProjection,
                this,
                typeof(TProjection),
                null,
                null,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied) { return Unauthorized(); }

        return Ok(await _multipleAggregateProjectionService.GetProjectionAsync<TProjection, TProjectionContents>());
    }
}
