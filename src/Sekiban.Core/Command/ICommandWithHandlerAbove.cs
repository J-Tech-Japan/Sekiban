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
public record EventOrNone<TAggregatePayload>(IEventPayloadApplicableTo<TAggregatePayload>? Value, bool HasValue = true)
    where TAggregatePayload : IAggregatePayloadCommon
{
    public static EventOrNone<TAggregatePayload> Empty => new(default, false);
    public static ResultBox<EventOrNone<TAggregatePayload>> None => Empty;
    public static EventOrNone<TAggregatePayload> FromValue(IEventPayloadApplicableTo<TAggregatePayload> value) =>
        new(value);
    public static ResultBox<EventOrNone<TAggregatePayload>> Event(IEventPayloadApplicableTo<TAggregatePayload> value) =>
        ResultBox.FromValue(FromValue(value));

    public IEventPayloadApplicableTo<TAggregatePayload> GetValue() =>
        HasValue && Value is not null ? Value : throw new ResultsInvalidOperationException("no value");

    public static implicit operator EventOrNone<TAggregatePayload>(UnitValue value) => Empty;
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
