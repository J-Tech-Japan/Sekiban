using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICreateCommandHandler<T, C> : ICommandHandler
    where T : IAggregatePayload, new() where C : ICreateCommand<T>
{
    public Task<CommandResponse> HandleAsync(CommandDocument<C> commandDocument, Aggregate<T> aggregateState);
    C CleanupCommandIfNeeded(C command);
}
