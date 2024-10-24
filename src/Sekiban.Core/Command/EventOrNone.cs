using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
namespace Sekiban.Core.Command;

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
