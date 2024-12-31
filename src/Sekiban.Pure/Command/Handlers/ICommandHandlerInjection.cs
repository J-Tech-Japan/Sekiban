using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.Command.Handlers;

public interface
    ICommandHandlerInjection<TCommand, TInjection, TAggregatePayload> : ICommandHandlerCommon<TCommand, TInjection,
    TAggregatePayload> where TCommand : ICommand, IEquatable<TCommand> where TAggregatePayload : IAggregatePayload
{
    public ResultBox<EventOrNone> Handle(
        TCommand command,
        TInjection injection,
        ICommandContext<TAggregatePayload> context);
}
