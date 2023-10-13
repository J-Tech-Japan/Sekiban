using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Dependency;
namespace Sekiban.Web.Controllers.Bases;

/// <summary>
///     Base multi projection list query controller
/// </summary>
/// <param name="queryExecutor"></param>
/// <param name="webDependencyDefinition"></param>
/// <param name="serviceProvider"></param>
/// <typeparam name="TProjectionPayload"></typeparam>
/// <typeparam name="TQuery"></typeparam>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
[ApiController]
[Produces("application/json")]
// ReSharper disable once UnusedTypeParameter
public class BaseMultiProjectionQueryController<TProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
    IQueryExecutor queryExecutor,
    IWebDependencyDefinition webDependencyDefinition,
    IServiceProvider serviceProvider) : ControllerBase where TProjectionPayload : IMultiProjectionPayloadCommon
    where TQuery : IMultiProjectionQuery<TProjectionPayload, TQueryParameter, TQueryResponse>
    where TQueryParameter : IQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
    protected readonly IQueryExecutor QueryExecutor = queryExecutor;

    [HttpGet]
    [Route("")]
    public async Task<ActionResult<TQueryResponse>> GetQueryResult([FromQuery] TQueryParameter queryParam)
    {
        if (webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.MultiProjection,
                this,
                typeof(TProjectionPayload),
                null,
                null,
                HttpContext,
                serviceProvider) ==
            AuthorizeResultType.Denied)
        {
            return Unauthorized();
        }
        var result = await QueryExecutor.ExecuteAsync(queryParam);
        return Ok(result);
    }
}
