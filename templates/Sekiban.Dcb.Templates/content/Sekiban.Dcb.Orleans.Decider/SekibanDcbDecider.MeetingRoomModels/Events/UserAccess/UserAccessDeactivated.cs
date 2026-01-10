using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.UserAccess;

public record UserAccessDeactivated(
    Guid UserId,
    string? Reason,
    DateTime DeactivatedAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new UserAccessTag(UserId));
}
