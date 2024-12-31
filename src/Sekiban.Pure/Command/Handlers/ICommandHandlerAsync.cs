using ResultBoxes;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.Command.Handlers;

public interface
    ICommandHandlerAsync<TCommand, TAggregatePayload> : ICommandHandlerCommon<TCommand, NoInjection, TAggregatePayload>
    where TCommand : ICommand, IEquatable<TCommand> where TAggregatePayload : IAggregatePayload
{
    public Task<ResultBox<EventOrNone>> HandleAsync(TCommand command, ICommandContext<TAggregatePayload> context);
}
