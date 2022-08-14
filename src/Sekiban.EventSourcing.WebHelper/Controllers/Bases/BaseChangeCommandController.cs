using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.WebHelper.Authorizations;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class BaseChangeCommandController<TAggregate, TAggregateContents, TAggregateCommand> : ControllerBase
    where TAggregate : TransferableAggregateBase<TAggregateContents>, new()
    where TAggregateContents : IAggregateContents, new()
    where TAggregateCommand : ChangeAggregateCommandBase<TAggregate>
{
    private readonly IAuthorizeDefinitionCollection _authorizeDefinitionCollection;
    private readonly IAggregateCommandExecutor _executor;
    public BaseChangeCommandController(IAggregateCommandExecutor executor, IAuthorizeDefinitionCollection authorizeDefinitionCollection)
    {
        _executor = executor;
        _authorizeDefinitionCollection = authorizeDefinitionCollection;
    }

    [HttpPatch]
    [Route("")]
    public virtual async Task<ActionResult<AggregateCommandExecutorResponse<TAggregateContents, TAggregateCommand>>> Execute(
        [FromBody] TAggregateCommand command)
    {
        if (_authorizeDefinitionCollection.CheckAuthorization(
                AuthorizeMethodType.ChangeCommand,
                this,
                typeof(TAggregate),
                typeof(TAggregateCommand),
                command,
                HttpContext) ==
            AuthorizeResultType.Denied) { return Unauthorized(); }
        return new ActionResult<AggregateCommandExecutorResponse<TAggregateContents, TAggregateCommand>>(
            await _executor.ExecChangeCommandAsync<TAggregate, TAggregateContents, TAggregateCommand>(command));
    }
}
