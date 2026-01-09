using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.UserDirectory;

public record UserReactivated(
    Guid UserId,
    DateTime ReactivatedAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new UserTag(UserId));
}
