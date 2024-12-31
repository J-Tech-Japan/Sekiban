using ResultBoxes;
namespace Sekiban.Pure.Events;

public record EventOrNone(IEventPayload? EventPayload, bool HasEvent)
{
    public static EventOrNone Empty => new(default, false);
    public static ResultBox<EventOrNone> None => Empty;
    public static EventOrNone FromValue(IEventPayload value) => new(value, true);
    public static ResultBox<EventOrNone> Event(IEventPayload value) => ResultBox.FromValue(FromValue(value));
    public IEventPayload GetValue() => HasEvent && EventPayload is not null
        ? EventPayload
        : throw new ResultsInvalidOperationException("no value");
    public static implicit operator EventOrNone(UnitValue value) => Empty;
}
