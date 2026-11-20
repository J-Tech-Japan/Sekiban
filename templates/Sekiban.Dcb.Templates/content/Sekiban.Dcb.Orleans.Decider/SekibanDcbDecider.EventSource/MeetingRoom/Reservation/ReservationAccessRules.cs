using Dcb.EventSource.MeetingRoom.User;
using Dcb.MeetingRoomModels.States.UserAccess;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;

namespace Dcb.EventSource.MeetingRoom.Reservation;

internal static class ReservationAccessRules
{
    internal static async Task<bool> IsAdminAsync(ICommandContext context, Guid userId)
    {
        if (userId == Guid.Empty)
        {
            return false;
        }

        var accessState = await context.GetStateAsync<UserAccessProjector>(new UserAccessTag(userId));
        return accessState.Payload is UserAccessState.UserAccessActive active && active.HasRole("Admin");
    }
}
