using Dcb.EventSource.MeetingRoom.User;
using Dcb.MeetingRoomModels.States.UserDirectory;
using Dcb.MeetingRoomModels.States.UserMonthlyReservation;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;

namespace Dcb.EventSource.MeetingRoom.Reservation;

internal static class ReservationCreationRules
{
    /// <param name="organizerIsAdmin">
    ///     Whether the organizer (the acting user) is an administrator, from the authenticated role.
    ///     Administrators are exempt from the booking window and the monthly limit.
    /// </param>
    internal static async Task ValidateAsync(ICommandContext context, Guid organizerId, DateTime startTime, bool organizerIsAdmin)
    {
        if (!organizerIsAdmin)
        {
            ValidateReservationMonth(startTime, DateTime.UtcNow);

            var monthlyLimit = await GetMonthlyReservationLimitAsync(context, organizerId);
            var monthTag = UserMonthlyReservationTag.FromStartTime(organizerId, startTime);
            var monthlyStateTyped = await context.GetStateAsync<UserMonthlyReservationProjector>(monthTag);
            var monthlyState = monthlyStateTyped.Payload as UserMonthlyReservationState ?? UserMonthlyReservationState.Empty;
            if (monthlyState.ActiveRequestCount >= monthlyLimit)
            {
                throw new ApplicationException($"Monthly reservation limit exceeded ({monthlyLimit}).");
            }
        }
    }

    private static async Task<int> GetMonthlyReservationLimitAsync(ICommandContext context, Guid userId)
    {
        var directoryState = await context.GetStateAsync<UserDirectoryProjector>(new UserTag(userId));
        var limit = directoryState.Payload switch
        {
            UserDirectoryState.UserDirectoryActive active => active.MonthlyReservationLimit,
            UserDirectoryState.UserDirectoryDeactivated => throw new ApplicationException("User is deactivated."),
            _ => UserDirectoryState.DefaultMonthlyReservationLimit
        };

        if (limit <= 0)
        {
            throw new ApplicationException("Monthly reservation limit is not configured for this user.");
        }

        return limit;
    }

    private static void ValidateReservationMonth(DateTime startTime, DateTime nowUtc)
    {
        var startMonth = new DateOnly(startTime.Year, startTime.Month, 1);
        var currentMonth = new DateOnly(nowUtc.Year, nowUtc.Month, 1);
        var nextMonth = currentMonth.AddMonths(1);

        if (startMonth != currentMonth && startMonth != nextMonth)
        {
            throw new ApplicationException("Reservations can only be made for this month or next month.");
        }
    }
}
