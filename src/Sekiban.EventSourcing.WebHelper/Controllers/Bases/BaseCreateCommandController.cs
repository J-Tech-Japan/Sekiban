using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

[ApiController]
public class BaseCreateCommandController<TAggregate, TAggregateContents, TAggregateCommand> : ControllerBase
    where TAggregate : TransferableAggregateBase<TAggregateContents>, new()
    where TAggregateContents : IAggregateContents, new()
    where TAggregateCommand : ICreateAggregateCommand<TAggregate>
{
    private readonly IAggregateCommandExecutor _executor;
    public BaseCreateCommandController(IAggregateCommandExecutor executor) =>
        _executor = executor;

    [HttpPost]
    [Route("")]
    public virtual async Task<ActionResult<AggregateCommandExecutorResponse<TAggregateContents, TAggregateCommand>>> Execute(
        [FromBody] TAggregateCommand command) =>
        new(await _executor.ExecCreateCommandAsync<TAggregate, TAggregateContents, TAggregateCommand>(command));
}
