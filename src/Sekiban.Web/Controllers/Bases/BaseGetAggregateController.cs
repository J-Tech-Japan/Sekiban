using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Dependency;
namespace Sekiban.Web.Controllers.Bases;

/// <summary>
///     Base get aggregate controller
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
[ApiController]
[Produces("application/json")]
// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class BaseGetAggregateController<TAggregatePayload>(
    IAggregateLoader aggregateLoader,
    IWebDependencyDefinition webDependencyDefinition,
    IServiceProvider serviceProvider) : ControllerBase where TAggregatePayload : IAggregatePayloadCommon
{

    [HttpGet]
    [Route("get/{id}")]
    public virtual async Task<ActionResult<AggregateState<TAggregatePayload>>> GetAsync(
        Guid id,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        string? includesSortableUniqueId = null)
    {
        if (webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.Get,
                this,
                typeof(TAggregatePayload),
                null,
                null,
                HttpContext,
                serviceProvider) ==
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
        if (webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.Get,
                this,
                typeof(TAggregatePayload),
                null,
                null,
                HttpContext,
                serviceProvider) ==
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
        if (webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.Get,
                this,
                typeof(TAggregatePayload),
                null,
                null,
                HttpContext,
                serviceProvider) ==
            AuthorizeResultType.Denied)
        {
            return Unauthorized();
        }
        var result = ids.Select(id => aggregateLoader.AsDefaultStateAsync<TAggregatePayload>(id));
        return Ok(result);
    }
}
