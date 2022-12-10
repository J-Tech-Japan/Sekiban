namespace Sekiban.Core.Event;

public interface IEventPayloadConvertingTo<TNewEventPayload> where TNewEventPayload : IEventPayloadCommon
{
    TNewEventPayload ConvertTo();
}
