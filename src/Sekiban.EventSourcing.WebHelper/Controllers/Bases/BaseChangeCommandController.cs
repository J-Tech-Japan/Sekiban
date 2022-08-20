using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.WebHelper.Authorizations;
using Sekiban.EventSourcing.WebHelper.Common;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases
{
    [ApiController]
    [Produces("application/json")]
    public class BaseChangeCommandController<TAggregate, TAggregateContents, TAggregateCommand> : ControllerBase
        where TAggregate : TransferableAggregateBase<TAggregateContents>, new()
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
        public virtual async Task<ActionResult<AggregateCommandExecutorResponse<TAggregateContents, TAggregateCommand>>> Execute(
            [FromBody] TAggregateCommand command)
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
            return new ActionResult<AggregateCommandExecutorResponse<TAggregateContents, TAggregateCommand>>(
                await _executor.ExecChangeCommandAsync<TAggregate, TAggregateContents, TAggregateCommand>(command));
        }
    }
}
