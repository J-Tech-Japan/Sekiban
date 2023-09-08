using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Documents;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Dependency;
namespace Sekiban.Web.Controllers.Bases;

/// <summary>
///     Base single projection controller
/// </summary>
/// <param name="aggregateLoader"></param>
/// <param name="webDependencyDefinition"></param>
/// <param name="serviceProvider"></param>
/// <typeparam name="TSingleProjectionPayload"></typeparam>
[ApiController]
[Produces("application/json")]
public class BaseSingleProjectionController<TSingleProjectionPayload>(
    IAggregateLoader aggregateLoader,
    IWebDependencyDefinition webDependencyDefinition,
    IServiceProvider serviceProvider) : ControllerBase where TSingleProjectionPayload : class, ISingleProjectionPayloadCommon
{

    [HttpGet]
    [Route("get/{id}")]
    public virtual async Task<ActionResult<SingleProjectionState<TSingleProjectionPayload>?>> GetAsync(
        Guid id,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        string? includesSortableUniqueId = null)
    {
        if (webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.SingleProjection,
                this,
                typeof(TSingleProjectionPayload),
                null,
                null,
                HttpContext,
                serviceProvider) ==
            AuthorizeResultType.Denied)
        {
            return Unauthorized();
        }
        var result = await aggregateLoader.AsSingleProjectionStateAsync<TSingleProjectionPayload>(
            id,
            rootPartitionKey,
            toVersion,
            includesSortableUniqueId);
        return Ok(result);
    }
    [HttpGet]
    [Route("getWithoutSnapshot/{id}")]
    public virtual async Task<ActionResult<SingleProjectionState<TSingleProjectionPayload>?>> GetWithoutSnapshotAsync(
        Guid id,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null)
    {
        if (webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.SingleProjection,
                this,
                typeof(TSingleProjectionPayload),
                null,
                null,
                HttpContext,
                serviceProvider) ==
            AuthorizeResultType.Denied)
        {
            return Unauthorized();
        }
        var result = await aggregateLoader.AsSingleProjectionStateFromInitialAsync<TSingleProjectionPayload>(id, rootPartitionKey, toVersion);
        return Ok(result);
    }
}
