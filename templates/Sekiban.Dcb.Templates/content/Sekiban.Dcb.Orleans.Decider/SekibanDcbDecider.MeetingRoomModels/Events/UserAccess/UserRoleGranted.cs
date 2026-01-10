using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.UserAccess;

public record UserRoleGranted(
    Guid UserId,
    string Role,
    DateTime GrantedAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new UserAccessTag(UserId));
}
