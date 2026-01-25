using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.EquipmentReservation;

public record EquipmentCheckedOut(
    Guid EquipmentReservationId,
    Guid EquipmentItemId,
    Guid CheckedOutBy,
    DateTime CheckedOutAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, [new EquipmentReservationTag(EquipmentReservationId), new EquipmentItemTag(EquipmentItemId)]);
}
