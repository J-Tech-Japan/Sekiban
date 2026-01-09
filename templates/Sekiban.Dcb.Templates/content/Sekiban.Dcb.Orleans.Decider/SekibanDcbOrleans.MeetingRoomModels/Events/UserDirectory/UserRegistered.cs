using Dcb.MeetingRoomModels.States.UserDirectory;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.UserDirectory;

public record UserRegistered(
    Guid UserId,
    string DisplayName,
    string Email,
    string? Department,
    DateTime RegisteredAt,
    int MonthlyReservationLimit = UserDirectoryState.DefaultMonthlyReservationLimit) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, new UserTag(UserId));
}
