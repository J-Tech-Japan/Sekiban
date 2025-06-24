using Dapr.Actors;
using ResultBoxes;
using Sekiban.Pure.Command;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;

namespace Sekiban.Pure.Dapr.Actors;

public interface IAggregateActor : IActor
{
    Task<ResultBox<CommandResponse>> ExecuteCommandAsync(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null);
    
    Task<ResultBox<IEnumerable<IEvent>>> GetEventsAsync();
}