using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Dependency;
namespace Sekiban.Web.Controllers.Bases;

/// <summary>
///     Aggregate list query controller base
/// </summary>
/// <param name="queryExecutor"></param>
/// <param name="serviceProvider"></param>
/// <param name="webDependencyDefinition"></param>
/// <typeparam name="TAggregatePayload"></typeparam>
/// <typeparam name="TQuery"></typeparam>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
[ApiController]
[Produces("application/json")]
// ReSharper disable once UnusedTypeParameter
public class BaseAggregateListQueryController<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse>(
    IQueryExecutor queryExecutor,
    IServiceProvider serviceProvider,
    IWebDependencyDefinition webDependencyDefinition) : ControllerBase where TAggregatePayload : IAggregatePayloadCommon
    where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
    where TQueryParameter : IListQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
    protected readonly IQueryExecutor QueryExecutor = queryExecutor;

    [HttpGet]
    [Route("")]
    public async Task<ActionResult<ListQueryResult<TQueryResponse>>> GetQueryResult([FromQuery] TQueryParameter queryParam)
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
        var result = await QueryExecutor.ExecuteAsync(queryParam);
        return Ok(result);
    }
}
