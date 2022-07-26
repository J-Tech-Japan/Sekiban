using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.WebHelper.Controllers.Bases;

[ApiController]
[Route("{controller}")]
[ApiExplorerSettings(IgnoreApi = true)]
public class BaseCreateCommandController<TAggregate, TAggregateContents, TAggregateCommand>
    where TAggregate : TransferableAggregateBase<TAggregateContents>, new()
    where TAggregateContents : IAggregateContents, new()
    where TAggregateCommand : ICreateAggregateCommand<TAggregate>
{
    private readonly IAggregateCommandExecutor _executor;
    public BaseCreateCommandController(IAggregateCommandExecutor executor) =>
        _executor = executor;

    [HttpPost]
    public async Task<ActionResult<AggregateCommandExecutorResponse<TAggregateContents, TAggregateCommand>>> Execute(
        [FromBody] TAggregateCommand command) =>
        new(await _executor.ExecCreateCommandAsync<TAggregate, TAggregateContents, TAggregateCommand>(command));
}
