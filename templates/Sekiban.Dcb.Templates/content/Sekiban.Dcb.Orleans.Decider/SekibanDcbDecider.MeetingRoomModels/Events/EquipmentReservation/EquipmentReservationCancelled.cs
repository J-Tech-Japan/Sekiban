using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.EquipmentReservation;

public record EquipmentReservationCancelled(
    Guid EquipmentReservationId,
    string Reason,
    DateTime CancelledAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new EquipmentReservationTag(EquipmentReservationId));
}
