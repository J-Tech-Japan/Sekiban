using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

[ApiController]
public class BaseChangeCommandController<TAggregate, TAggregateContents, TAggregateCommand> : ControllerBase
    where TAggregate : TransferableAggregateBase<TAggregateContents>, new()
    where TAggregateContents : IAggregateContents, new()
    where TAggregateCommand : ChangeAggregateCommandBase<TAggregate>
{
    private readonly IAggregateCommandExecutor _executor;
    public BaseChangeCommandController(IAggregateCommandExecutor executor) =>
        _executor = executor;

    [HttpPatch]
    [Route("")]
    public virtual async Task<ActionResult<AggregateCommandExecutorResponse<TAggregateContents, TAggregateCommand>>> Execute(
        [FromBody] TAggregateCommand command) =>
        new(await _executor.ExecChangeCommandAsync<TAggregate, TAggregateContents, TAggregateCommand>(command));
}
