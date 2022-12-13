namespace Sekiban.Core.Event;

public interface IEventPayloadConvertingTo<out TNewEventPayload> where TNewEventPayload : IEventPayloadCommon
{
    TNewEventPayload ConvertTo();
}
