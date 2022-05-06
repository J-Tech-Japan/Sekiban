using CustomerDomainContext.Aggregates.LoyaltyPoints;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Commands;
using CustomerDomainContext.Aggregates.LoyaltyPoints.ValueObjects;
namespace CustomerWebApi.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class LoyaltyPointController : Controller
{
    private readonly AggregateCommandExecutor _aggregateCommandExecutor;
    private readonly SingleAggregateService _singleAggregateService;
    private readonly SnapshotListWriter _snapshotListWriter;

    public LoyaltyPointController(
        AggregateCommandExecutor aggregateCommandExecutor,
        SnapshotListWriter snapshotListWriter,
        SingleAggregateService aggregateService)
    {
        _aggregateCommandExecutor = aggregateCommandExecutor;
        _snapshotListWriter = snapshotListWriter;
        _singleAggregateService = aggregateService;
    }

    [HttpGet]
    public async Task<ActionResult<LoyaltyPointDto>> GetAsync(Guid clientId) =>
        await _singleAggregateService.GetAggregateDtoAsync<LoyaltyPoint, LoyaltyPointDto>(clientId) ?? throw new JJInvalidArgumentException();

    [HttpGet]
    public async Task<ActionResult<IEnumerable<LoyaltyPointDto>>> ListAsync(QueryListType queryListType = QueryListType.ActiveOnly) =>
        new(await _singleAggregateService.DtoListAsync<LoyaltyPoint, LoyaltyPointDto>(queryListType));

    [HttpGet]
    public async Task<ActionResult<Dictionary<int, string>>> UsageTypesAsync() => new(
        await Task.FromResult(LoyaltyPointUsageType.LoyaltyPointUsageTypes));

    [HttpGet]
    public async Task<ActionResult<Dictionary<int, string>>> ReceiveTypesAsync() => new(
        await Task.FromResult(LoyaltyPointReceiveType.LoyaltyPointReceiveTypes));

    [HttpPatch]
    public async Task<IActionResult> AddPointAsync([FromBody] AddLoyaltyPoint command)
    {
        var result = await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointDto, AddLoyaltyPoint>(command);
        return Ok(result);
    }

    [HttpPatch]
    public async Task<IActionResult> UsePointAsync([FromBody] UseLoyaltyPoint command)
    {
        var result = await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointDto, UseLoyaltyPoint>(command);
        return Ok(result);
    }
}
