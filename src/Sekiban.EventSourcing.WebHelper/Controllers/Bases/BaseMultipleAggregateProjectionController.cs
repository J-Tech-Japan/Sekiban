using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.WebHelper.Authorizations;
using Sekiban.EventSourcing.WebHelper.Common;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases
{
    [Produces("application/json")]
    public class BaseMultipleAggregateProjectionController<TProjection> : ControllerBase
        where TProjection : MultipleAggregateProjectionBase<TProjection>, IMultipleAggregateProjectionDto, new()
    {
        private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
        private readonly SekibanControllerOptions _sekibanControllerOptions;
        private readonly IServiceProvider _serviceProvider;
        public BaseMultipleAggregateProjectionController(
            IMultipleAggregateProjectionService multipleAggregateProjectionService,
            SekibanControllerOptions sekibanControllerOptions,
            IServiceProvider serviceProvider)
        {
            _multipleAggregateProjectionService = multipleAggregateProjectionService;
            _sekibanControllerOptions = sekibanControllerOptions;
            _serviceProvider = serviceProvider;
        }

        [HttpGet]
        [Route("")]
        public virtual async Task<ActionResult<TProjection>> GetMultipleProjectionAsync()
        {
            if (_sekibanControllerOptions.AuthorizeDefinitionCollection.CheckAuthorization(
                    AuthorizeMethodType.MultipleAggregateProjection,
                    this,
                    typeof(TProjection),
                    null,
                    null,
                    HttpContext,
                    _serviceProvider) ==
                AuthorizeResultType.Denied) { return Unauthorized(); }

            return Ok(await _multipleAggregateProjectionService.GetProjectionAsync<TProjection>());
        }
    }
}
