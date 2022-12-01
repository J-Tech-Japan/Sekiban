using Sekiban.Core.Aggregate;

namespace Sekiban.Core.Command;

public interface
    IVersionValidationCommandHandler<TAggregatePayload, TCommand> : ICommandHandler<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayload, new() where TCommand : IVersionValidationCommand<TAggregatePayload>
{
}
