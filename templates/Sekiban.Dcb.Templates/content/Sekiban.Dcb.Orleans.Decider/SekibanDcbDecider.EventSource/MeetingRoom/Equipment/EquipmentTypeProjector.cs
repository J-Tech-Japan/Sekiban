using Dcb.MeetingRoomModels.Events.EquipmentType;
using Dcb.MeetingRoomModels.States.EquipmentType;
using Dcb.MeetingRoomModels.States.EquipmentType.Deciders;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.MeetingRoom.Equipment;

public class EquipmentTypeProjector : ITagProjector<EquipmentTypeProjector>
{
    public static string ProjectorVersion => "1.0.0";
    public static string ProjectorName => nameof(EquipmentTypeProjector);

    public static ITagStatePayload Project(ITagStatePayload current, Event ev)
    {
        var state = current as EquipmentTypeState ?? EquipmentTypeState.Empty;

        return ev.Payload switch
        {
            EquipmentTypeCreated created => state.Evolve(created),
            EquipmentTypeUpdated updated => state.Evolve(updated),
            _ => state
        };
    }
}
