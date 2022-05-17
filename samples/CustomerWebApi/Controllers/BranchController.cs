using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Commands;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace CustomerWebApi.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class BranchController : Controller
{
    private readonly IAggregateCommandExecutor _aggregateCommandExecutor;
    private readonly SingleAggregateService _aggregateService;
    private readonly MultipleAggregateProjectionService _multipleAggregateProjectionService;

    public BranchController(
        IAggregateCommandExecutor aggregateCommandExecutor,
        SingleAggregateService aggregateService,
        MultipleAggregateProjectionService multipleAggregateProjectionService)
    {
        _aggregateCommandExecutor = aggregateCommandExecutor;
        _aggregateService = aggregateService;
        _multipleAggregateProjectionService = multipleAggregateProjectionService;
    }

    [HttpPost]
    public async Task<ActionResult<AggregateCommandExecutorResponse<BranchDto, CreateBranch>>> CreateAsync(CreateBranch command)
    {
        var result = await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchDto, CreateBranch>(command);
        return result;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<BranchDto>>> ListAsync() =>
        new(await _multipleAggregateProjectionService.GetAggregateList<Branch, BranchDto>());
}
