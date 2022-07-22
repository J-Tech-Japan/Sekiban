using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Commands;
using CustomerDomainContext.Aggregates.Clients.Projections;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace CustomerWebApi.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class ClientController : Controller
{
    private readonly IAggregateCommandExecutor _aggregateCommandExecutor;
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    private readonly ISingleAggregateService _singleAggregateService;
    public ClientController(
        IAggregateCommandExecutor aggregateCommandExecutor,
        ISingleAggregateService aggregateService,
        IMultipleAggregateProjectionService multipleAggregateProjectionService)
    {
        _aggregateCommandExecutor = aggregateCommandExecutor;
        _singleAggregateService = aggregateService;
        _multipleAggregateProjectionService = multipleAggregateProjectionService;
    }

    [HttpGet]
    public async Task<ActionResult<AggregateDtoBase<ClientContents>>> GetAsync(Guid clientId) =>
        await _singleAggregateService.GetAggregateDtoAsync<Client, ClientContents>(clientId) ?? throw new SekibanInvalidArgumentException();

    [HttpGet]
    public async Task<ActionResult<ClientNameHistoryProjection>> GetNameHistoryAsync(Guid clientId) =>
        await _singleAggregateService.GetProjectionAsync<ClientNameHistoryProjection>(clientId) ?? throw new SekibanInvalidArgumentException();

    // [HttpPost]
    // public async Task<IActionResult> TakeSnapshotAsync(Guid branchId)
    // {
    //     await _snapshotListWriter.TakeSnapshot<Client, ClientDto>(
    //         new AggregateIdPartitionKeyFactory(branchId));
    //     return Ok();
    // }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AggregateDtoBase<ClientContents>>>>
        ListAsync(QueryListType queryListType = QueryListType.ActiveOnly) =>
        new(await _multipleAggregateProjectionService.GetAggregateList<Client, ClientContents>(queryListType));

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClientNameHistoryProjection>>> ListChangeNamesAsync(
        QueryListType queryListType = QueryListType.ActiveOnly) =>
        new(await _multipleAggregateProjectionService.GetSingleAggregateProjectionList<ClientNameHistoryProjection>(queryListType));

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CreateClient command)
    {
        var createdResult = await _aggregateCommandExecutor.ExecCreateCommandAsync<Client, ClientContents, CreateClient>(command);
        return Ok(createdResult);
    }

    [HttpPatch]
    public async Task<IActionResult> ChangeNameAsync(ChangeClientName command)
    {
        var result = await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ClientContents, ChangeClientName>(command);
        return Ok(result);
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAsync(DeleteClient command)
    {
        await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ClientContents, DeleteClient>(command);
        return Ok();
    }
}
