using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface IChangeCommandHandler<T, C> : ICommandHandler
    where T : IAggregatePayload, new() where C : ChangeCommandBase<T>
{
    public Task<CommandResponse> HandleAsync(CommandDocument<C> commandDocument, Aggregate<T> aggregate);
    public Task<CommandResponse> HandleForOnlyPublishingCommandAsync(CommandDocument<C> commandDocument, Guid aggregateId);
    C CleanupCommandIfNeeded(C command);
}
