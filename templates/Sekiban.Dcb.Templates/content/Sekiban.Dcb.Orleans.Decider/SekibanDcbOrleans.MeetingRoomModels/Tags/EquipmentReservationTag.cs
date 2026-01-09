using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.Tags;

public record EquipmentReservationTag(Guid EquipmentReservationId) : IGuidTagGroup<EquipmentReservationTag>
{
    public bool IsConsistencyTag() => true;
    public static string TagGroupName => "EquipmentReservation";
    public static EquipmentReservationTag FromContent(string content) => new(Guid.Parse(content));
    public Guid GetId() => EquipmentReservationId;
}
