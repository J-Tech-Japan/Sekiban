using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Dependency;
namespace Sekiban.Web.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class BaseGetAggregateController<TAggregatePayload> : ControllerBase where TAggregatePayload : IAggregatePayloadCommon
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebDependencyDefinition _webDependencyDefinition;
    private readonly IAggregateLoader aggregateLoader;

    public BaseGetAggregateController(
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
    public virtual async Task<ActionResult<AggregateState<TAggregatePayload>>> GetAsync(
        Guid id,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        string? includesSortableUniqueId = null)
    {
        if (_webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.Get,
                this,
                typeof(TAggregatePayload),
                null,
                null,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied)
        {
            return Unauthorized();
        }
        var result = await aggregateLoader.AsDefaultStateAsync<TAggregatePayload>(id, rootPartitionKey, toVersion, includesSortableUniqueId);
        return result is null ? NotFound() : Ok(result);
    }
    [HttpGet]
    [Route("getWithoutSnapshot/{id}")]
    public virtual async Task<ActionResult<AggregateState<TAggregatePayload>>> GetWithoutSnapshotAsync(
        Guid id,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null)
    {
        if (_webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.Get,
                this,
                typeof(TAggregatePayload),
                null,
                null,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied)
        {
            return Unauthorized();
        }
        return Ok(await aggregateLoader.AsDefaultStateFromInitialAsync<TAggregatePayload>(id, rootPartitionKey, toVersion));
    }

    [HttpGet]
    [Route("getids")]
    public virtual async Task<ActionResult<IEnumerable<AggregateState<TAggregatePayload>>>> GetIdsAsync([FromQuery] IEnumerable<Guid> ids)
    {
        await Task.CompletedTask;
        if (_webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.Get,
                this,
                typeof(TAggregatePayload),
                null,
                null,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied)
        {
            return Unauthorized();
        }
        var result = ids.Select(id => aggregateLoader.AsDefaultStateAsync<TAggregatePayload>(id));
        return Ok(result);
    }
}
