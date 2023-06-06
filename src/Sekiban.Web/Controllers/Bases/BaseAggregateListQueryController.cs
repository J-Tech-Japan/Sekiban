using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Dependency;
namespace Sekiban.Web.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class BaseAggregateListQueryController<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse> : ControllerBase
    where TAggregatePayload : IAggregatePayloadCommon
    where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
    where TQueryParameter : IListQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebDependencyDefinition _webDependencyDefinition;
    protected readonly IQueryExecutor QueryExecutor;

    public BaseAggregateListQueryController(
        IQueryExecutor queryExecutor,
        IServiceProvider serviceProvider,
        IWebDependencyDefinition webDependencyDefinition)
    {
        QueryExecutor = queryExecutor;
        _serviceProvider = serviceProvider;
        _webDependencyDefinition = webDependencyDefinition;
    }

    [HttpGet]
    [Route("")]
    public async Task<ActionResult<ListQueryResult<TQueryResponse>>> GetQueryResult([FromQuery] TQueryParameter queryParam)
    {
        if (_webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.Get,
                this,
                typeof(TAggregatePayload),
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
