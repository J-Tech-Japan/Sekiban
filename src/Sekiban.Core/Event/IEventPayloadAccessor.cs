namespace Sekiban.Core.Event;

public interface IEventPayloadAccessor
{
    public IEventPayloadCommon GetPayload();
    public T? GetPayload<T>() where T : class, IEventPayloadCommon;
}
