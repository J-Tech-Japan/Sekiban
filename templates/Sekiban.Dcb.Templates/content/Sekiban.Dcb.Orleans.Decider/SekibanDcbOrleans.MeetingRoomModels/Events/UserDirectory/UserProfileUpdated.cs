using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.UserDirectory;

public record UserProfileUpdated(
    Guid UserId,
    string DisplayName,
    string Email,
    string? Department) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new UserTag(UserId));
}
