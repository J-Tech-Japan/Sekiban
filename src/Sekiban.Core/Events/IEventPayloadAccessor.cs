namespace Sekiban.Core.Events;

public interface IEventPayloadAccessor
{
    public IEventPayloadCommon GetPayload();
    public T? GetPayload<T>() where T : class, IEventPayloadCommon;
}
