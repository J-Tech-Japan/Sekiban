using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.UserAccess;

public record UserRoleRevoked(
    Guid UserId,
    string Role,
    DateTime RevokedAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new UserAccessTag(UserId));
}
