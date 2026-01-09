using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.Tags;

public record ApprovalRequestTag(Guid ApprovalRequestId) : IGuidTagGroup<ApprovalRequestTag>
{
    public bool IsConsistencyTag() => true;
    public static string TagGroupName => "ApprovalRequest";
    public static ApprovalRequestTag FromContent(string content) => new(Guid.Parse(content));
    public Guid GetId() => ApprovalRequestId;
}
