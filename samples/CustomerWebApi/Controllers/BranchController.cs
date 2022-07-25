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
    private readonly ISingleAggregateService _aggregateService;
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;

    public BranchController(
        IAggregateCommandExecutor aggregateCommandExecutor,
        ISingleAggregateService aggregateService,
        IMultipleAggregateProjectionService multipleAggregateProjectionService)
    {
        _aggregateCommandExecutor = aggregateCommandExecutor;
        _aggregateService = aggregateService;
        _multipleAggregateProjectionService = multipleAggregateProjectionService;
    }

    [HttpPost]
    public async Task<ActionResult<AggregateCommandExecutorResponse<BranchContents, CreateBranch>>> CreateAsync(CreateBranch command)
    {
        var result = await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchContents, CreateBranch>(command.BranchId, command);
        return result;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AggregateDto<BranchContents>>>> ListAsync() =>
        new(await _multipleAggregateProjectionService.GetAggregateList<Branch, BranchContents>());
}
