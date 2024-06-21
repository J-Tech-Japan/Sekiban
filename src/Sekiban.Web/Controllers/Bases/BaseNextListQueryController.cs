using Microsoft.AspNetCore.Mvc;
using ResultBoxes;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Types;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Dependency;
namespace Sekiban.Web.Controllers.Bases;

[ApiController]
[Produces("application/json")]
// ReSharper disable once UnusedTypeParameter
public class BaseNextListQueryController<TQuery, TQueryResponse>(
    IQueryExecutor queryExecutor,
    IServiceProvider serviceProvider,
    IWebDependencyDefinition webDependencyDefinition)
    : ControllerBase where TQuery : INextListQueryCommon<TQueryResponse> where TQueryResponse : notnull
{
    [HttpGet]
    [Route("")]
    public async Task<ActionResult<ListQueryResult<TQueryResponse>>> GetQueryResult([FromQuery] TQuery query)
    {
        if (webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.Get,
                this,
                typeof(TQuery).GetAggregatePayloadFromQueryNext() ?? typeof(TQuery).GetMultiProjectionPayloadFromQueryNext() ?? typeof(TQuery),
                null,
                null,
                HttpContext,
                serviceProvider) ==
            AuthorizeResultType.Denied)
        {
            return Unauthorized();
        }
        var result = await queryExecutor.ExecuteAsync(query);
        return Ok(result.UnwrapBox());
    }
}