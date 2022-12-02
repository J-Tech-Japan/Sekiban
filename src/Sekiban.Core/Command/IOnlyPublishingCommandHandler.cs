using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;

namespace Sekiban.Core.Command;

public interface
    IOnlyPublishingCommandHandler<TAggregatePayload, TCommand> : ICommandHandlerCommon<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayload, new() where TCommand : IOnlyPublishingCommand<TAggregatePayload>
{
    public IAsyncEnumerable<IEventPayload<TAggregatePayload>> HandleCommandAsync(Guid aggregateId, TCommand command);
}
