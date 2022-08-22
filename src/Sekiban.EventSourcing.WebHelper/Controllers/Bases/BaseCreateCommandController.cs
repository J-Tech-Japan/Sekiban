using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.WebHelper.Authorizations;
using Sekiban.EventSourcing.WebHelper.Common;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class BaseCreateCommandController<TAggregate, TAggregateContents, TAggregateCommand> : ControllerBase
    where TAggregate : TransferableAggregateBase<TAggregateContents>, new()
    where TAggregateContents : IAggregateContents, new()
    where TAggregateCommand : ICreateAggregateCommand<TAggregate>
{
    private readonly IAggregateCommandExecutor _executor;
    private readonly SekibanControllerOptions _sekibanControllerOptions;
    private readonly IServiceProvider _serviceProvider;
    public BaseCreateCommandController(
        IAggregateCommandExecutor executor,
        SekibanControllerOptions sekibanControllerOptions,
        IServiceProvider serviceProvider)
    {
        _executor = executor;
        _sekibanControllerOptions = sekibanControllerOptions;
        _serviceProvider = serviceProvider;
    }

    [HttpPost]
    [Route("")]
    public virtual async Task<ActionResult<AggregateCommandExecutorResponse<TAggregateContents, TAggregateCommand>>> Execute(
        [FromBody] TAggregateCommand command)
    {
        if (_sekibanControllerOptions.AuthorizeDefinitionCollection.CheckAuthorization(
                AuthorizeMethodType.CreateCommand,
                this,
                typeof(TAggregate),
                typeof(TAggregateCommand),
                command,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied) { return Unauthorized(); }

        var response = await _executor.ExecCreateCommandAsync<TAggregate, TAggregateContents, TAggregateCommand>(command);
        if (response.ValidationResults.Any())
        {
            foreach (var validationResult in response.ValidationResults)
            {
                ModelState.AddModelError(validationResult.MemberNames.First(), validationResult.ErrorMessage);
            }
            return BadRequest(ModelState);
        }
        return new ActionResult<AggregateCommandExecutorResponse<TAggregateContents, TAggregateCommand>>(response);
    }
}
