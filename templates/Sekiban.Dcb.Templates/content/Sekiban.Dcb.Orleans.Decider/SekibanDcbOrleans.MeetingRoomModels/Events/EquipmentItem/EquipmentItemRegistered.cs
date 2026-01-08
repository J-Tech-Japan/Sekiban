using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.EquipmentItem;

public record EquipmentItemRegistered(
    Guid EquipmentItemId,
    Guid EquipmentTypeId,
    string SerialNumber,
    string? Notes,
    DateTime RegisteredAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, [new EquipmentItemTag(EquipmentItemId), new EquipmentTypeTag(EquipmentTypeId)]);
}
