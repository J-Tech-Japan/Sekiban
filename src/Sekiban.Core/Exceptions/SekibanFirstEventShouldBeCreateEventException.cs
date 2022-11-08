namespace Sekiban.Core.Exceptions;

public class SekibanFirstEventShouldBeCreateEventException : Exception, ISekibanException
{
    public SekibanFirstEventShouldBeCreateEventException(Type eventType) : base(
        $"The first event {eventType.Name} should be a create event, please inherit ICreatedEvent<AggregatePayload>")
    {
    }
}
