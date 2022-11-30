using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
namespace Sekiban.Core.Command;

public interface IOnlyPublishingCommandHandlerBase<TAggregatePayload, TCommand> : ICommandHandler<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayload, new() where TCommand : IOnlyPublishingCommandBase<TAggregatePayload>
{
    public IAsyncEnumerable<IEventPayload<TAggregatePayload>> HandleCommandAsync(Guid aggregateId, TCommand command);
}
