using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Web.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class BaseAggregateQueryController<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse> : ControllerBase
    where TAggregatePayload : IAggregatePayloadCommon
    where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
    where TQueryParameter : IQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
    protected readonly IQueryExecutor QueryExecutor;

    public BaseAggregateQueryController(IQueryExecutor queryExecutor)
    {
        QueryExecutor = queryExecutor;
    }

    [HttpGet]
    [Route("")]
    public async Task<ActionResult<TQueryResponse>> GetQueryResult([FromQuery] TQueryParameter queryParam)
    {
        var result = await QueryExecutor
            .ExecuteAsync(queryParam);
        return Ok(result);
    }
}
