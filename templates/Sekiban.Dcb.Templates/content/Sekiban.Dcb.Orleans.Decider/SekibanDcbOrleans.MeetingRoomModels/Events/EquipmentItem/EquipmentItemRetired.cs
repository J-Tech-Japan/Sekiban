using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.EquipmentItem;

public record EquipmentItemRetired(
    Guid EquipmentItemId,
    Guid EquipmentTypeId,
    string Reason,
    DateTime RetiredAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, [new EquipmentItemTag(EquipmentItemId), new EquipmentTypeTag(EquipmentTypeId)]);
}
