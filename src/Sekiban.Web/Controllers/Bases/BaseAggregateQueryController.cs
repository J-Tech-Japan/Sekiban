using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Web.Controllers.Bases;

/// <summary>
///     Base aggregate list query controller
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
/// <typeparam name="TQuery"></typeparam>
/// <typeparam name="TQueryParameter"></typeparam>
/// <typeparam name="TQueryResponse"></typeparam>
[ApiController]
[Produces("application/json")]
// ReSharper disable once UnusedTypeParameter
public class BaseAggregateQueryController<TAggregatePayload, TQuery, TQueryParameter, TQueryResponse> : ControllerBase
    where TAggregatePayload : IAggregatePayloadCommon
    where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
    where TQueryParameter : IQueryParameter<TQueryResponse>, IEquatable<TQueryParameter>
    where TQueryResponse : IQueryResponse
{
    protected readonly IQueryExecutor QueryExecutor;

    public BaseAggregateQueryController(IQueryExecutor queryExecutor) => QueryExecutor = queryExecutor;

    [HttpGet]
    [Route("")]
    public async Task<ActionResult<TQueryResponse>> GetQueryResult([FromQuery] TQueryParameter queryParam)
    {
        var result = await QueryExecutor.ExecuteAsync(queryParam);
        return Ok(result);
    }
}
