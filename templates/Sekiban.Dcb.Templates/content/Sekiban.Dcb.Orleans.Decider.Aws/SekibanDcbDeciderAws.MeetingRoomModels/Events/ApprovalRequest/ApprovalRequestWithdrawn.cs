using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.ApprovalRequest;

/// <summary>
///     The reservation was cancelled while its approval request was still pending,
///     so the request is closed without a decision.
/// </summary>
public record ApprovalRequestWithdrawn(
    Guid ApprovalRequestId,
    Guid ReservationId,
    string Reason,
    DateTime WithdrawnAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, [new ApprovalRequestTag(ApprovalRequestId), new ReservationTag(ReservationId)]);
}
