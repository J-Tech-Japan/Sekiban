using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.WebHelper.Controllers.Bases;

[ApiController]
[ApiExplorerSettings(IgnoreApi = false)]
public class BaseCreateCommandController<TAggregate, TAggregateContents, TAggregateCommand>
    where TAggregate : TransferableAggregateBase<TAggregateContents>, new()
    where TAggregateContents : IAggregateContents, new()
    where TAggregateCommand : ICreateAggregateCommand<TAggregate>
{
    private readonly IAggregateCommandExecutor _executor;
    public BaseCreateCommandController(IAggregateCommandExecutor executor) =>
        _executor = executor;

    [HttpPost]
    [Route("")]
    public async Task<ActionResult<AggregateCommandExecutorResponse<TAggregateContents, TAggregateCommand>>> Execute(
        [FromBody] TAggregateCommand command) =>
        new(await _executor.ExecCreateCommandAsync<TAggregate, TAggregateContents, TAggregateCommand>(command));
}
