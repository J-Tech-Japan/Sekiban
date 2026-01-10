using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.EquipmentType;

public record EquipmentTypeCreated(
    Guid EquipmentTypeId,
    string Name,
    string Description,
    int TotalQuantity,
    int MaxPerReservation) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new EquipmentTypeTag(EquipmentTypeId));
}
