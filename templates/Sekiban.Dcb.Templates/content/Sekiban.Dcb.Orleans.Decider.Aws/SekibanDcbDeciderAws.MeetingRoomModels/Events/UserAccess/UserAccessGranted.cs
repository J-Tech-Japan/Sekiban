using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.UserAccess;

public record UserAccessGranted(
    Guid UserId,
    string InitialRole,
    DateTime GrantedAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new UserAccessTag(UserId));
}
