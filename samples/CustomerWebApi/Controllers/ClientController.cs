using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Commands;
using CustomerDomainContext.Aggregates.Clients.Projections;
namespace CustomerWebApi.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class ClientController : Controller
{
    private readonly AggregateCommandExecutor _aggregateCommandExecutor;
    private readonly SingleAggregateService _singleAggregateService;
    private readonly SnapshotListWriter _snapshotListWriter;

    public ClientController(
        AggregateCommandExecutor aggregateCommandExecutor,
        SnapshotListWriter snapshotListWriter,
        SingleAggregateService aggregateService)
    {
        _aggregateCommandExecutor = aggregateCommandExecutor;
        _snapshotListWriter = snapshotListWriter;
        _singleAggregateService = aggregateService;
    }

    [HttpGet]
    public async Task<ActionResult<ClientDto>> GetAsync(Guid clientId) =>
        await _singleAggregateService
            .GetAggregateDtoAsync<Client, ClientDto>(clientId) ??
        throw new JJInvalidArgumentException();

    [HttpGet]
    public async Task<ActionResult<ClientNameHistoryProjection>>
        GetNameHistoryAsync(Guid clientId) =>
        await _singleAggregateService
            .GetProjectionAsync<ClientNameHistoryProjection>(clientId) ??
        throw new JJInvalidArgumentException();

    // [HttpPost]
    // public async Task<IActionResult> TakeSnapshotAsync(Guid branchId)
    // {
    //     await _snapshotListWriter.TakeSnapshot<Client, ClientDto>(
    //         new AggregateIdPartitionKeyFactory(branchId));
    //     return Ok();
    // }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClientDto>>> ListAsync(
        QueryListType queryListType = QueryListType.ActiveOnly) => new(
        await _singleAggregateService.DtoListAsync<Client, ClientDto>(queryListType));

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClientNameHistoryProjection>>> ListChangeNamesAsync(
        QueryListType queryListType = QueryListType.ActiveOnly) => new(
        await _singleAggregateService.DtoListAsync<ClientNameHistoryProjection>(queryListType));

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CreateClient command)
    {
        var createdResult =
            await _aggregateCommandExecutor.ExecCreateCommandAsync<Client, ClientDto, CreateClient>(
                command);
        return Ok(createdResult);
    }

    [HttpPatch]
    public async Task<IActionResult> ChangeNameAsync(ChangeClientName command)
    {
        var result =
            await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ClientDto, ChangeClientName>(command);
        return Ok(result);
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAsync(DeleteClient command)
    {
        await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ClientDto, DeleteClient>(command);
        return Ok();
    }
}
