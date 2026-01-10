using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.States.ApprovalRequest;
using Dcb.MeetingRoomModels.States.ApprovalRequest.Deciders;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.MeetingRoom.ApprovalRequest;

public class ApprovalRequestProjector : ITagProjector<ApprovalRequestProjector>
{
    public static string ProjectorVersion => "1.0.0";
    public static string ProjectorName => nameof(ApprovalRequestProjector);

    public static ITagStatePayload Project(ITagStatePayload current, Event ev)
    {
        var state = current as ApprovalRequestState ?? ApprovalRequestState.Empty;

        return ev.Payload switch
        {
            ApprovalFlowStarted started => state.Evolve(started),
            ApprovalDecisionRecorded decision => state.Evolve(decision),
            _ => state
        };
    }
}
