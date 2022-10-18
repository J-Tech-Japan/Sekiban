using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Sekiban.Addon.Web.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class
    BaseProjectionListQueryFilterController<TProjection, TProjectionContents, TQueryFilter, TQueryParameter, TQueryFilterResponse> : ControllerBase
    where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
    where TProjectionContents : IMultipleAggregateProjectionContents, new()
    where TQueryFilter : IProjectionListQueryFilterDefinition<TProjection, TProjectionContents, TQueryParameter, TQueryFilterResponse>, new()
    where TQueryParameter : IQueryParameter
{
    protected readonly IQueryFilterService _queryFilterService;
    public BaseProjectionListQueryFilterController(IQueryFilterService queryFilterService)
    {
        _queryFilterService = queryFilterService;
    }
    [HttpGet]
    [Route("")]
    public async Task<ActionResult<QueryFilterListResult<TQueryFilterResponse>>> GetQueryResult([FromQuery] TQueryParameter queryParam)
    {
        var result = await _queryFilterService
            .GetProjectionListQueryFilterAsync<TProjection, TProjectionContents, TQueryFilter, TQueryParameter, TQueryFilterResponse>(queryParam);
        return Ok(result);
    }
}
