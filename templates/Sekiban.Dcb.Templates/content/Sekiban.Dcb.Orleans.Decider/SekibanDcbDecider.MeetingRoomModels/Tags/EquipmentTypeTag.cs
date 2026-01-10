using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.Tags;

public record EquipmentTypeTag(Guid EquipmentTypeId) : IGuidTagGroup<EquipmentTypeTag>
{
    public bool IsConsistencyTag() => true;
    public static string TagGroupName => "EquipmentType";
    public static EquipmentTypeTag FromContent(string content) => new(Guid.Parse(content));
    public Guid GetId() => EquipmentTypeId;
}
