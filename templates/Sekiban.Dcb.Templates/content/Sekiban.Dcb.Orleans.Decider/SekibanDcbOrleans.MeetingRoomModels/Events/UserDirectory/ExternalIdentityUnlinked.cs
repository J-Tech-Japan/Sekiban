using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.UserDirectory;

/// <summary>
///     Event for unlinking an external identity from a user
/// </summary>
public record ExternalIdentityUnlinked(
    Guid UserId,
    string Provider,
    string ExternalId,
    DateTime UnlinkedAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new UserTag(UserId));
}
