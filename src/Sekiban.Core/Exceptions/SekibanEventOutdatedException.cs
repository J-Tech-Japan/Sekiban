namespace Sekiban.Core.Exceptions;

public class SekibanEventOutdatedException : Exception, ISekibanException
{
    public SekibanEventOutdatedException(Type outdatedEventPayloadType) : base(outdatedEventPayloadType.Name + " event has been outdated")
    {
    }
}
