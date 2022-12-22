namespace Sekiban.Core.Events;

public interface IEventPayloadConvertingTo<out TNewEventPayload> where TNewEventPayload : IEventPayloadCommon
{
    TNewEventPayload ConvertTo();
}
