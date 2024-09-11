using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Dependency;
namespace Sekiban.Web.Controllers.Bases;

/// <summary>
///     Base general list query controller
/// </summary>
/// <typeparam name="TQuery"></typeparam>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
[ApiController]
[Produces("application/json")]
public class BaseGeneralListQueryController<TQuery, TQueryParameter, TQueryResponse> : ControllerBase
    where TQuery : IGeneralListQuery<TQueryParameter, TQueryResponse>
    where TQueryParameter : IListQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
    where TQueryResponse : IQueryResponse
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebDependencyDefinition _webDependencyDefinition;
    protected readonly IQueryExecutor QueryExecutor;

    public BaseGeneralListQueryController(
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
    public async Task<ActionResult<ListQueryResult<TQueryResponse>>> GetQueryResult(
        [FromQuery] TQueryParameter queryParam)
    {
        if (await _webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.MultiProjection,
                this,
                typeof(TQuery),
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
