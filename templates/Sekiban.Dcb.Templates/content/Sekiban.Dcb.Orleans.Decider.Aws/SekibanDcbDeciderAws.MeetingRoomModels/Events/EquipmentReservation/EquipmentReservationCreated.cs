using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.Events.EquipmentReservation;

public record EquipmentReservationCreated(
    Guid EquipmentReservationId,
    Guid EquipmentTypeId,
    int Quantity,
    Guid? RoomReservationId,
    Guid RequesterId,
    DateTime StartTime,
    DateTime EndTime) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags()
    {
        var tags = new List<ITag>
        {
            new EquipmentReservationTag(EquipmentReservationId),
            new EquipmentTypeTag(EquipmentTypeId)
        };
        if (RoomReservationId.HasValue)
            tags.Add(new ReservationTag(RoomReservationId.Value));
        return new(this, tags);
    }
}
