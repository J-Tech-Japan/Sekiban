using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.Tags;

public record EquipmentItemTag(Guid EquipmentItemId) : IGuidTagGroup<EquipmentItemTag>
{
    public bool IsConsistencyTag() => true;
    public static string TagGroupName => "EquipmentItem";
    public static EquipmentItemTag FromContent(string content) => new(Guid.Parse(content));
    public Guid GetId() => EquipmentItemId;
}
