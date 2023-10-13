using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Web.Controllers.Bases;

/// <summary>
///     base single projection query controller
/// </summary>
/// <typeparam name="TSingleProjectionPayload"></typeparam>
/// <typeparam name="TQuery"></typeparam>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
[ApiController]
[Produces("application/json")]
// ReSharper disable once UnusedTypeParameter
public class BaseSingleProjectionQueryController<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>
    (IQueryExecutor queryExecutor) : ControllerBase where TSingleProjectionPayload : ISingleProjectionPayloadCommon
    where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
    where TQueryParameter : IQueryParameter<TQueryResponse>
    where TQueryResponse : IQueryResponse
{
    protected readonly IQueryExecutor QueryExecutor = queryExecutor;

    [HttpGet]
    [Route("")]
    public async Task<ActionResult<TQueryResponse>> GetQueryResult([FromQuery] TQueryParameter queryParam)
    {
        var result = await QueryExecutor.ExecuteAsync(queryParam);
        return Ok(result);
    }
}
