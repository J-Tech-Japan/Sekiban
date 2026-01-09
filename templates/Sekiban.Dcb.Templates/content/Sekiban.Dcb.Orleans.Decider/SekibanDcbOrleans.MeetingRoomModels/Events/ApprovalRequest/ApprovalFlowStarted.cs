using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.ApprovalRequest;

public record ApprovalFlowStarted(
    Guid ApprovalRequestId,
    Guid ReservationId,
    Guid RoomId,
    Guid RequesterId,
    List<Guid> ApproverIds,
    DateTime RequestedAt,
    string? RequestComment) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, [new ApprovalRequestTag(ApprovalRequestId), new ReservationTag(ReservationId)]);
}
