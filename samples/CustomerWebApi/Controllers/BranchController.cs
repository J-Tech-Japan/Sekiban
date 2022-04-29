using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Commands;
namespace CustomerWebApi.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class BranchController : Controller
{
    private readonly AggregateCommandExecutor _aggregateCommandExecutor;
    private readonly SingleAggregateService _aggregateService;
    private readonly SnapshotListWriter _snapshotListWriter;

    public BranchController(
        AggregateCommandExecutor aggregateCommandExecutor,
        SingleAggregateService aggregateService,
        SnapshotListWriter snapshotListWriter)
    {
        _aggregateCommandExecutor = aggregateCommandExecutor;
        _aggregateService = aggregateService;
        _snapshotListWriter = snapshotListWriter;
    }

    [HttpPost]
    public async Task<ActionResult<AggregateCommandExecutorResponse<BranchDto, CreateBranch>>>
        CreateAsync(CreateBranch command)
    {
        var result = await _aggregateCommandExecutor
            .ExecConstructorAsync<Branch, BranchDto, CreateBranch>(command);
        return result;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BranchDto>>> ListAsync() =>
        new(
            await _aggregateService.DtoListAsync<Branch, BranchDto>());

    // [HttpPost]
    // public async Task<IActionResult> TakeSnapshotAsync()
    // {
    //     await _snapshotListWriter.TakeSnapshot<Branch, BranchDto>(
    //         new ClassPartitionKeyFactory<Branch, BranchDto>());
    //     return Ok();
    // }
}
