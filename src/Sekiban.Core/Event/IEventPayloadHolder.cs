namespace Sekiban.Core.Event;

public interface IEventPayloadHolder
{
    public IEventPayload GetPayload();
    public T? GetPayload<T>() where T : class, IEventPayload;
}
