using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Dependency;
namespace Sekiban.Web.Controllers.Bases;

[ApiController]
[Produces("application/json")]
// ReSharper disable once UnusedTypeParameter
public class BaseMultiProjectionQueryController<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse> : ControllerBase
    where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    where TQuery : IMultiProjectionQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
    where TQueryParameter : IQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebDependencyDefinition _webDependencyDefinition;
    protected readonly IQueryExecutor QueryExecutor;

    public BaseMultiProjectionQueryController(
        IQueryExecutor queryExecutor,
        IWebDependencyDefinition webDependencyDefinition,
        IServiceProvider serviceProvider)
    {
        QueryExecutor = queryExecutor;
        _webDependencyDefinition = webDependencyDefinition;
        _serviceProvider = serviceProvider;
    }

    [HttpGet]
    [Route("")]
    public async Task<ActionResult<TQueryResponse>> GetQueryResult([FromQuery] TQueryParameter queryParam)
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
        var result = await QueryExecutor.ExecuteAsync(queryParam);
        return Ok(result);
    }
}
