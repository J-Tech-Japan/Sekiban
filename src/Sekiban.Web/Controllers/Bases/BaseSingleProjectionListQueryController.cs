using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Dependency;
namespace Sekiban.Web.Controllers.Bases;

/// <summary>
///     Base single projection list query controller
/// </summary>
/// <param name="queryExecutor"></param>
/// <param name="webDependencyDefinition"></param>
/// <param name="serviceProvider"></param>
/// <typeparam name="TSingleProjectionPayload"></typeparam>
/// <typeparam name="TQuery"></typeparam>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
[ApiController]
[Produces("application/json")]
// ReSharper disable once UnusedTypeParameter
public class BaseSingleProjectionListQueryController<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
    IQueryExecutor queryExecutor,
    IWebDependencyDefinition webDependencyDefinition,
    IServiceProvider serviceProvider) : ControllerBase where TSingleProjectionPayload : ISingleProjectionPayloadCommon
    where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
    where TQueryParameter : IListQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
    protected readonly IQueryExecutor QueryExecutor = queryExecutor;

    [HttpGet]
    [Route("")]
    public async Task<ActionResult<ListQueryResult<TQueryResponse>>> GetQueryResult([FromQuery] TQueryParameter queryParam)
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
        var result = await QueryExecutor.ExecuteAsync(queryParam);
        return Ok(result);
    }
}
