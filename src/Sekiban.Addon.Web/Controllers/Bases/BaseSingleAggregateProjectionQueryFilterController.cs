using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Addon.Web.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class BaseSingleAggregateProjectionQueryFilterController<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents,
    TQueryFilter, TQueryFilterParameter, TQueryFilterResponse> : ControllerBase where TAggregate : AggregateCommonBase, new()
    where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>, new
    ()
    where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
    where TQueryFilter : ISingleAggregateProjectionQueryFilterDefinition<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents,
        TQueryFilterParameter, TQueryFilterResponse>, new()
    where TQueryFilterParameter : IQueryParameter
{
    protected readonly IQueryFilterService _queryFilterService;
    public BaseSingleAggregateProjectionQueryFilterController(IQueryFilterService queryFilterService)
    {
        _queryFilterService = queryFilterService;
    }
    [HttpGet]
    [Route("")]
    public async Task<ActionResult<TQueryFilterResponse>> GetQueryResult([FromQuery] TQueryFilterParameter queryParam)
    {
        var result = await _queryFilterService
            .GetSingleAggregateProjectionQueryFilterAsync<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents, TQueryFilter,
                TQueryFilterParameter, TQueryFilterResponse>(queryParam);
        return Ok(result);
    }
}
