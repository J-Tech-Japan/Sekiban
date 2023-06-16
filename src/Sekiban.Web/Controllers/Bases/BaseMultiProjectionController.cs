using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Dependency;
namespace Sekiban.Web.Controllers.Bases;

[Produces("application/json")]
public class BaseMultiProjectionController<TProjectionPayload> : ControllerBase where TProjectionPayload : IMultiProjectionPayloadCommon, new()
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebDependencyDefinition _webDependencyDefinition;
    private readonly IMultiProjectionService multiProjectionService;

    public BaseMultiProjectionController(
        IMultiProjectionService multiProjectionService,
        IWebDependencyDefinition webDependencyDefinition,
        IServiceProvider serviceProvider)
    {
        this.multiProjectionService = multiProjectionService;
        _webDependencyDefinition = webDependencyDefinition;
        _serviceProvider = serviceProvider;
    }

    [HttpGet]
    [Route("")]
    public virtual async Task<ActionResult<MultiProjectionState<TProjectionPayload>>> GetMultiProjectionAsync(
        string? includesSortableUniqueId = null,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions)
    {
        if (_webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.MultiProjection,
                this,
                typeof(TProjectionPayload),
                null,
                null,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied)
        {
            return Unauthorized();
        }

        return Ok(
            await multiProjectionService.GetMultiProjectionAsync<TProjectionPayload>(
                rootPartitionKey,
                SortableUniqueIdValue.NullableValue(includesSortableUniqueId)));
    }
}
