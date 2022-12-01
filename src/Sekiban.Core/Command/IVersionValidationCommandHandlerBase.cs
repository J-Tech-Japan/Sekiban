using Sekiban.Core.Aggregate;

namespace Sekiban.Core.Command;

public interface
    IVersionValidationCommandHandlerBase<TAggregatePayload, TCommand> : ICommandHandlerBase<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayload, new() where TCommand : IVersionValidationCommand<TAggregatePayload>
{
}
