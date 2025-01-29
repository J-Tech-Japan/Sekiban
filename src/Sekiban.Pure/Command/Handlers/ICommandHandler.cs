using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Events;

namespace Sekiban.Pure.Command.Handlers;

public interface
    ICommandHandler<TCommand, TAggregatePayload> : ICommandHandlerCommon<TCommand, NoInjection, TAggregatePayload>
    where TCommand : ICommand, IEquatable<TCommand> where TAggregatePayload : IAggregatePayload
{
    public ResultBox<EventOrNone> Handle(TCommand command, ICommandContext<TAggregatePayload> context);
}
