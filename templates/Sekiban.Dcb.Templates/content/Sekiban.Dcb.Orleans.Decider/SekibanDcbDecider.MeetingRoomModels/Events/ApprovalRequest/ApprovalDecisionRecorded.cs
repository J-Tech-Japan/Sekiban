using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.ApprovalRequest;

public enum ApprovalDecision
{
    Approved,
    Rejected
}

public record ApprovalDecisionRecorded(
    Guid ApprovalRequestId,
    Guid ReservationId,
    Guid ApproverId,
    ApprovalDecision Decision,
    string? Comment,
    DateTime DecidedAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, [new ApprovalRequestTag(ApprovalRequestId), new ReservationTag(ReservationId)]);
}
