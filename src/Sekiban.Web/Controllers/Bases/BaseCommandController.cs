using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Exceptions;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Dependency;
namespace Sekiban.Web.Controllers.Bases;

/// <summary>
///     Base command controller
/// </summary>
/// <param name="executor"></param>
/// <param name="webDependencyDefinition"></param>
/// <param name="serviceProvider"></param>
/// <typeparam name="TAggregatePayload"></typeparam>
/// <typeparam name="TCommand"></typeparam>
[ApiController]
[Produces("application/json")]
public class BaseCommandController<TAggregatePayload, TCommand>(
    ICommandExecutor executor,
    IWebDependencyDefinition webDependencyDefinition,
    IServiceProvider serviceProvider) : ControllerBase where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
{

    [HttpPost]
    [Route("")]
    public virtual async Task<ActionResult<CommandExecutorResponse>> Execute([FromBody] TCommand command)
    {
        if (webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.Command,
                this,
                typeof(TAggregatePayload),
                typeof(TCommand),
                command,
                HttpContext,
                serviceProvider) ==
            AuthorizeResultType.Denied)
        {
            return Unauthorized();
        }
        var response = await executor.ExecCommandAsync(command);
        if (response.ValidationResults is null || !response.ValidationResults.Any())
        {
            return new ActionResult<CommandExecutorResponse>(response);
        }
        throw new SekibanValidationErrorsException(response.ValidationResults!);
    }
}
