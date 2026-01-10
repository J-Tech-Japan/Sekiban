using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.UserDirectory;

/// <summary>
///     Event for linking an external identity (e.g., SSO) to a user
/// </summary>
public record ExternalIdentityLinked(
    Guid UserId,
    string Provider,
    string ExternalId,
    DateTime LinkedAt) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new UserTag(UserId));
}
