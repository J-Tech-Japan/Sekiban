namespace Sekiban.Core.Exceptions;

/// <summary>
///     This exception is thrown when event is outdated and have new version, but still called to apply.
/// </summary>
public class SekibanEventOutdatedException : Exception, ISekibanException
{
    public SekibanEventOutdatedException(Type outdatedEventPayloadType) : base(outdatedEventPayloadType.Name + " event has been outdated")
    {
    }
}
