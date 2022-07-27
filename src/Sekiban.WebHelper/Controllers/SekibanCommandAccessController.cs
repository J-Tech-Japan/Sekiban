using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Shared;
using Dependency = CustomerDomainContext.Shared.Dependency;
namespace Sekiban.WebHelper.Controllers;

[ApiController]
[Route("api/command")]
public class SekibanCommandAccessController : ControllerBase
{
    private readonly IAggregateCommandExecutor _executor;
    public SekibanCommandAccessController(IAggregateCommandExecutor executor) =>
        _executor = executor;
    [HttpPost]
    [Route("{aggregateName}/{commandName}")]
    public async Task<IActionResult> CreateCommandExecuterAsync(string aggregateName, string commandName, [FromBody] dynamic command)
    {
        var list = Dependency.GetDependencies();
        foreach (var (serviceType, implementationType) in list)
        {
            var interfaceType = serviceType;

            if (interfaceType?.Name == typeof(ICreateAggregateCommandHandler<,>).Name)
            {
                var aggregateType = interfaceType?.GenericTypeArguments[0];
                var commandType = interfaceType?.GenericTypeArguments[1];
                var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
                if (aggregateType == null || commandType == null || aggregateContentsType == null) { continue; }
                if (aggregateName.Trim().ToLower() != aggregateType.Name.ToLower() || commandName.Trim().ToLower() != commandType.Name.ToLower())
                {
                    continue;
                }

                string strjson = SekibanJsonHelper.Serialize(command);
                var commandObject = (dynamic?)SekibanJsonHelper.Deserialize(strjson, commandType);
                if (commandObject == null) { return Problem($"Aggregate Command could not serialize to {commandType.Name}"); }
                var createMethod = _executor.GetType().GetMethod(nameof(IAggregateCommandExecutor.ExecCreateCommandAsync));
                var methodInfo = createMethod?.MakeGenericMethod(aggregateType, aggregateContentsType, commandType);
                if (methodInfo == null) { return Problem("Aggregate Command can not execute."); }
                var responseTask = (Task?)methodInfo.Invoke(_executor, new object?[] { commandObject, null });
                if (responseTask == null) { return Problem("Aggregate Command can not execute."); }
                await responseTask;
                var resultProperty = responseTask.GetType().GetProperty("Result");
                if (resultProperty == null) { return Problem("Aggregate Command can not execute."); }
                var result = resultProperty.GetValue(responseTask);
                return Ok(result);
            }
        }
        await Task.CompletedTask;
        return Problem("Aggregate Command not found");
    }

    [HttpPost]
    [Route("{aggregateName}/{commandName}/{aggregateId}")]
    public async Task<IActionResult> ChangeCommandExecuterAsync(
        string aggregateName,
        string commandName,
        Guid aggregateId,
        [FromBody] dynamic command)
    {
        var list = Dependency.GetDependencies();
        foreach (var (serviceType, implementationType) in list)
        {
            var interfaceType = serviceType;

            if (interfaceType?.Name == typeof(IChangeAggregateCommandHandler<,>).Name)
            {
                var aggregateType = interfaceType?.GenericTypeArguments[0];
                var commandType = interfaceType?.GenericTypeArguments[1];
                var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
                if (aggregateType == null || commandType == null || aggregateContentsType == null) { continue; }
                if (aggregateName.Trim().ToLower() != aggregateType.Name.ToLower() || commandName.Trim().ToLower() != commandType.Name.ToLower())
                {
                    continue;
                }

                string strjson = SekibanJsonHelper.Serialize(command);
                var commandObject = (dynamic?)SekibanJsonHelper.Deserialize(strjson, commandType);
                if (commandObject == null) { return Problem($"Aggregate Command could not serialize to {commandType.Name}"); }
                var createMethod = _executor.GetType().GetMethod(nameof(IAggregateCommandExecutor.ExecChangeCommandAsync));
                var methodInfo = createMethod?.MakeGenericMethod(aggregateType, aggregateContentsType, commandType);
                if (methodInfo == null) { return Problem("Aggregate Command can not execute."); }
                var responseTask = (Task?)methodInfo.Invoke(_executor, new object?[] { aggregateId, commandObject, null });
                if (responseTask == null) { return Problem("Aggregate Command can not execute."); }
                await responseTask;
                var resultProperty = responseTask.GetType().GetProperty("Result");
                if (resultProperty == null) { return Problem("Aggregate Command can not execute."); }
                var result = resultProperty.GetValue(responseTask);
                return Ok(result);
            }
        }
        await Task.CompletedTask;
        return Problem("Aggregate Command not found");
    }
}
