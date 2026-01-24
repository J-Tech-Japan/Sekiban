using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.UserAccess;

public record UserAccessReactivated(
    Guid UserId,
    DateTime ReactivatedAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new UserAccessTag(UserId));
}
