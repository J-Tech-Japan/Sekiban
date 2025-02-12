using ResultBoxes;

namespace Sekiban.Pure.Events;

public record EventOrNone(IEventPayload? EventPayload, bool HasEvent)
{
    public static EventOrNone Empty => new(default, false);
    public static ResultBox<EventOrNone> None => Empty;

    public static EventOrNone FromValue(IEventPayload value)
    {
        return new EventOrNone(value, true);
    }

    public static ResultBox<EventOrNone> Event(IEventPayload value)
    {
        return ResultBox.FromValue(FromValue(value));
    }

    public IEventPayload GetValue()
    {
        return HasEvent && EventPayload is not null
            ? EventPayload
            : throw new ResultsInvalidOperationException("no value");
    }

    public static implicit operator EventOrNone(UnitValue value)
    {
        return Empty;
    }
}