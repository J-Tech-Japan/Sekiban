using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Addon.Web.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class
    BaseProjectionQueryFilterController<TProjection, TProjectionContents, TQueryFilter, TQueryParameter, TQueryFilterResponse> : ControllerBase
    where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
    where TProjectionContents : IMultipleAggregateProjectionContents, new()
    where TQueryFilter : IProjectionQueryFilterDefinition<TProjection, TProjectionContents, TQueryParameter, TQueryFilterResponse>, new()
    where TQueryParameter : IQueryParameter
{
    protected readonly IQueryFilterService _queryFilterService;
    public BaseProjectionQueryFilterController(IQueryFilterService queryFilterService)
    {
        _queryFilterService = queryFilterService;
    }
    [HttpGet]
    [Route("")]
    public async Task<ActionResult<TQueryFilterResponse>> GetQueryResult([FromQuery] TQueryParameter queryParam)
    {
        var result = await _queryFilterService
            .GetProjectionQueryFilterAsync<TProjection, TProjectionContents, TQueryFilter, TQueryParameter, TQueryFilterResponse>(queryParam);
        return Ok(result);
    }
}
