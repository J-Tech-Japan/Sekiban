using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.EquipmentReservation;

public record EquipmentItemAssigned(
    Guid EquipmentReservationId,
    Guid EquipmentItemId,
    DateTime AssignedAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, [new EquipmentReservationTag(EquipmentReservationId), new EquipmentItemTag(EquipmentItemId)]);
}
