using CustomerDomainContext.Aggregates.LoyaltyPoints;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Commands;
using CustomerDomainContext.Aggregates.LoyaltyPoints.ValueObjects;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace CustomerWebApi.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class LoyaltyPointController : Controller
{
    private readonly IAggregateCommandExecutor _aggregateCommandExecutor;
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    private readonly ISingleAggregateService _singleAggregateService;
    public LoyaltyPointController(
        IAggregateCommandExecutor aggregateCommandExecutor,
        ISingleAggregateService aggregateService,
        IMultipleAggregateProjectionService multipleAggregateProjectionService)
    {
        _aggregateCommandExecutor = aggregateCommandExecutor;
        _singleAggregateService = aggregateService;
        _multipleAggregateProjectionService = multipleAggregateProjectionService;
    }

    [HttpGet]
    public async Task<ActionResult<AggregateDtoBase<LoyaltyPointContents>>> GetAsync(Guid clientId) =>
        await _singleAggregateService.GetAggregateDtoAsync<LoyaltyPoint, LoyaltyPointContents>(clientId) ??
        throw new SekibanInvalidArgumentException();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AggregateDtoBase<LoyaltyPointContents>>>> ListAsync(
        QueryListType queryListType = QueryListType.ActiveOnly) =>
        new(await _multipleAggregateProjectionService.GetAggregateList<LoyaltyPoint, LoyaltyPointContents>(queryListType));

    [HttpGet]
    public async Task<ActionResult<Dictionary<int, string>>> UsageTypesAsync() =>
        new(await Task.FromResult(LoyaltyPointUsageType.LoyaltyPointUsageTypes));

    [HttpGet]
    public async Task<ActionResult<Dictionary<int, string>>> ReceiveTypesAsync() =>
        new(await Task.FromResult(LoyaltyPointReceiveType.LoyaltyPointReceiveTypes));

    [HttpPatch]
    public async Task<IActionResult> AddPointAsync([FromBody] AddLoyaltyPoint command)
    {
        var result = await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointContents, AddLoyaltyPoint>(command);
        return Ok(result);
    }

    [HttpPatch]
    public async Task<IActionResult> UsePointAsync([FromBody] UseLoyaltyPoint command)
    {
        var result = await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointContents, UseLoyaltyPoint>(command);
        return Ok(result);
    }
}
