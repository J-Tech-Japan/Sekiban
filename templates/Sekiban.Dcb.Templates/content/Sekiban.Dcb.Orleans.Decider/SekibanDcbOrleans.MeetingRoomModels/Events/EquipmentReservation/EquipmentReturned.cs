using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.EquipmentReservation;

public record EquipmentReturned(
    Guid EquipmentReservationId,
    Guid EquipmentItemId,
    Guid ReturnedBy,
    DateTime ReturnedAt,
    string? Condition) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, [new EquipmentReservationTag(EquipmentReservationId), new EquipmentItemTag(EquipmentItemId)]);
}
