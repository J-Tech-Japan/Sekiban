using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
namespace Sekiban.Core.Command;

public interface
    ICommandWithHandlerAbove<TAggregatePayload, in TCommand> : ICommandWithHandlerCommon<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayloadCommon
    where TCommand : ICommandCommon<TAggregatePayload>, IEquatable<TCommand>
{
    public static abstract ResultBox<EventOrNone<TAggregatePayload>> HandleCommand(
        TCommand command,
        ICommandContext<TAggregatePayload> context);
}
public static class EventOrNone
{
    public static EventOrNone<TAggregatePayload> FromValue<TAggregatePayload>(
        IEventPayloadApplicableTo<TAggregatePayload> value) where TAggregatePayload : IAggregatePayloadCommon =>
        new(value);
    public static ResultBox<EventOrNone<TAggregatePayload>> Event<TAggregatePayload>(
        IEventPayloadApplicableTo<TAggregatePayload> value) where TAggregatePayload : IAggregatePayloadCommon =>
        ResultBox.FromValue(new EventOrNone<TAggregatePayload>(value));

    public static ResultBox<EventOrNone<TAggregatePayload>> None<TAggregatePayload>()
        where TAggregatePayload : IAggregatePayloadCommon =>
        EventOrNone<TAggregatePayload>.None;
}
