using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Addon.Web.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class BaseSingleAggregateProjectionListQueryFilterController<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents,
    TQueryFilter, TQueryFilterParameter, TQueryFilterResponse> : ControllerBase where TAggregate : AggregateCommonBase, new()
    where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>, new
    ()
    where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
    where TQueryFilter : ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, TSingleAggregateProjection,
        TSingleAggregateProjectionContents, TQueryFilterParameter, TQueryFilterResponse>, new()
    where TQueryFilterParameter : IQueryParameter
{
    protected readonly IQueryFilterService _queryFilterService;
    public BaseSingleAggregateProjectionListQueryFilterController(IQueryFilterService queryFilterService)
    {
        _queryFilterService = queryFilterService;
    }
    [HttpGet]
    [Route("")]
    public async Task<ActionResult<QueryFilterListResult<TQueryFilterResponse>>> GetQueryResult([FromQuery] TQueryFilterParameter queryParam)
    {
        var result = await _queryFilterService
            .GetSingleAggregateProjectionListQueryFilterAsync<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents, TQueryFilter
                , TQueryFilterParameter, TQueryFilterResponse>(queryParam);
        return Ok(result);
    }
}
