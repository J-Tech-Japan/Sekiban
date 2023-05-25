using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Exceptions;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Dependency;
namespace Sekiban.Web.Controllers.Bases;

[ApiController]
[Produces("application/json")]
public class BaseCommandController<TAggregatePayload, TCommand> : ControllerBase
    where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ICommand<TAggregatePayload>
{
    private readonly ICommandExecutor _executor;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebDependencyDefinition _webDependencyDefinition;

    public BaseCommandController(
        ICommandExecutor executor,
        IWebDependencyDefinition webDependencyDefinition,
        IServiceProvider serviceProvider)
    {
        _executor = executor;
        _webDependencyDefinition = webDependencyDefinition;
        _serviceProvider = serviceProvider;
    }

    [HttpPost]
    [Route("")]
    public virtual async Task<ActionResult<CommandExecutorResponse>> Execute([FromBody] TCommand command)
    {
        if (_webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.ChangeCommand,
                this,
                typeof(TAggregatePayload),
                typeof(TCommand),
                command,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied)
        {
            return Unauthorized();
        }
        var response = await _executor.ExecCommandAsync(command);
        if (response.ValidationResults is null || !response.ValidationResults.Any())
        {
            return new ActionResult<CommandExecutorResponse>(response);
        }
        throw new SekibanValidationErrorsException(response.ValidationResults!);
    }
}
