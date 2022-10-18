namespace Sekiban.Core.Event;

public interface IEventPayloadHolder
{
    public IEventPayload GetPayload();
}
