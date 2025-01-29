using Sekiban.Pure.Events;

namespace Sekiban.Pure.OrleansEventSourcing;

[GenerateSerializer]
public record OrleansEvent(
    [property:Id(0)]Guid Id,
    [property:Id(1)]IEventPayload Payload,
    [property:Id(2)]OrleansPartitionKeys PartitionKeys,
    [property:Id(3)]string SortableUniqueId,
    [property:Id(4)]int Version,
    [property:Id(5)]string EventPayloadTypeName)
{
    public static OrleansEvent FromEvent(IEvent ev)
    {
        var payload = ev.GetPayload();
        return new(
            ev.Id,
            payload,
            ev.PartitionKeys.ToOrleansPartitionKeys(),
            ev.SortableUniqueId,
            ev.Version,
            payload.GetType().Name); 
    }

}
