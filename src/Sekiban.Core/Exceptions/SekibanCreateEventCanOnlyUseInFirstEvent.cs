namespace Sekiban.Core.Exceptions;

public class SekibanCreateEventCanOnlyUseInFirstEvent : Exception, ISekibanException
{
    public SekibanCreateEventCanOnlyUseInFirstEvent(Type eventType, int version) : base(
        $"CreateEvent can only use in first event. current version: {version} {eventType.Name} may need to inherit IChangeEvent<AggregatePayload>")
    {
    }
}
