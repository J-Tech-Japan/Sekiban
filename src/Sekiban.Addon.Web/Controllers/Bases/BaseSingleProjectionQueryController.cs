using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Addon.Web.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class BaseSingleProjectionQueryController<TSingleProjectionPayload,
    TQuery, TQueryParameter, TQueryResponse> : ControllerBase
    where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    where TQuery : ISingleProjectionQuery<TSingleProjectionPayload,
        TQueryParameter, TQueryResponse>
    where TQueryParameter : IQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
    protected readonly IQueryExecutor QueryExecutor;

    public BaseSingleProjectionQueryController(IQueryExecutor queryExecutor) => QueryExecutor = queryExecutor;

    [HttpGet]
    [Route("")]
    public async Task<ActionResult<TQueryResponse>> GetQueryResult([FromQuery] TQueryParameter queryParam)
    {
        var result = await QueryExecutor
            .ExecuteAsync(queryParam);
        return Ok(result);
    }
}
