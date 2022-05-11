using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Commands;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace CustomerWebApi.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class BranchController : Controller
{
    private readonly IAggregateCommandExecutor _aggregateCommandExecutor;
    private readonly SingleAggregateService _aggregateService;

    public BranchController(IAggregateCommandExecutor aggregateCommandExecutor, SingleAggregateService aggregateService)
    {
        _aggregateCommandExecutor = aggregateCommandExecutor;
        _aggregateService = aggregateService;
    }

    [HttpPost]
    public async Task<ActionResult<AggregateCommandExecutorResponse<BranchDto, CreateBranch>>> CreateAsync(CreateBranch command)
    {
        var result = await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchDto, CreateBranch>(command);
        return result;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BranchDto>>> ListAsync() =>
        new(await _aggregateService.DtoListAsync<Branch, BranchDto>());
}
