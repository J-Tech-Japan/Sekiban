using Microsoft.AspNetCore.Mvc;
using Sekiban.Addon.Web.Authorizations;
using Sekiban.Addon.Web.Dependency;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Addon.Web.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class BaseSingleProjectionController<TSingleProjectionPayload> : ControllerBase
    where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebDependencyDefinition _webDependencyDefinition;
    private readonly IAggregateLoader aggregateLoader;

    public BaseSingleProjectionController(
        IAggregateLoader aggregateLoader,
        IWebDependencyDefinition webDependencyDefinition,
        IServiceProvider serviceProvider)
    {
        this.aggregateLoader = aggregateLoader;
        _webDependencyDefinition = webDependencyDefinition;
        _serviceProvider = serviceProvider;
    }

    [HttpGet]
    [Route("get/{id}")]
    public virtual async Task<ActionResult<SingleProjectionState<TSingleProjectionPayload>?>> GetAsync(
        Guid id,
        int? toVersion = null,
        string? includesSortableUniqueId = null)
    {
        if (_webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.SingleProjection,
                this,
                typeof(TSingleProjectionPayload),
                null,
                null,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied)
        {
            return Unauthorized();
        }
        var result = await aggregateLoader.AsSingleProjectionStateAsync<TSingleProjectionPayload>(
            id,
            toVersion,
            includesSortableUniqueId);
        return Ok(result);
    }
    [HttpGet]
    [Route("getWithoutSnapshot/{id}")]
    public virtual async Task<ActionResult<SingleProjectionState<TSingleProjectionPayload>?>> GetWithoutSnapshotAsync(
        Guid id,
        int? toVersion = null)
    {
        if (_webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.SingleProjection,
                this,
                typeof(TSingleProjectionPayload),
                null,
                null,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied)
        {
            return Unauthorized();
        }
        var result = await aggregateLoader.AsSingleProjectionStateFromInitialAsync<TSingleProjectionPayload>(
            id,
            toVersion);
        return Ok(result);
    }
}
