using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Dependency;
namespace Sekiban.Web.Controllers.Bases;

/// <summary>
///     Base general query controller
/// </summary>
/// <typeparam name="TQuery"></typeparam>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
[ApiController]
[Produces("application/json")]
public class BaseGeneralQueryController<TQuery, TQueryParameter, TQueryResponse> : ControllerBase
    where TQuery : IGeneralQuery<TQueryParameter, TQueryResponse>
    where TQueryParameter : IQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebDependencyDefinition _webDependencyDefinition;
    protected readonly IQueryExecutor QueryExecutor;

    public BaseGeneralQueryController(
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
