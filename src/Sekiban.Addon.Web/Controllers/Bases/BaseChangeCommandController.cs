using Microsoft.AspNetCore.Mvc;
using Sekiban.Addon.Web.Authorizations;
using Sekiban.Addon.Web.Common;
using Sekiban.Addon.Web.Exceptions;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
namespace Sekiban.Addon.Web.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class BaseChangeCommandController<TAggregate, TAggregateContents, TAggregateCommand> : ControllerBase
    where TAggregate : AggregateBase<TAggregateContents>, new()
    where TAggregateContents : IAggregateContents, new()
    where TAggregateCommand : ChangeAggregateCommandBase<TAggregate>
{
    private readonly IAggregateCommandExecutor _executor;
    private readonly SekibanControllerOptions _sekibanControllerOptions;
    private readonly IServiceProvider _serviceProvider;
    public BaseChangeCommandController(
        IAggregateCommandExecutor executor,
        SekibanControllerOptions sekibanControllerOptions,
        IServiceProvider serviceProvider)
    {
        _executor = executor;
        _sekibanControllerOptions = sekibanControllerOptions;
        _serviceProvider = serviceProvider;
    }

    [HttpPatch]
    [Route("")]
    public virtual async Task<ActionResult<AggregateCommandExecutorResponse>> Execute([FromBody] TAggregateCommand command)
    {
        if (_sekibanControllerOptions.AuthorizeDefinitionCollection.CheckAuthorization(
                AuthorizeMethodType.ChangeCommand,
                this,
                typeof(TAggregate),
                typeof(TAggregateCommand),
                command,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied) { return Unauthorized(); }
        var (response, events) = await _executor.ExecChangeCommandAsync<TAggregate, TAggregateContents, TAggregateCommand>(command);
        if (response.ValidationResults is null || !response.ValidationResults.Any())
        {
            return new ActionResult<AggregateCommandExecutorResponse>(response);
        }
        throw new SekibanValidationErrorsException(response.ValidationResults!);
    }
}