using Dcb.MeetingRoomModels.Events.UserDirectory;
using Dcb.MeetingRoomModels.States.UserDirectory;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.User;

public record UpdateUserMonthlyReservationLimit : ICommandWithHandler<UpdateUserMonthlyReservationLimit>
{
    [Required]
    public Guid UserId { get; init; }

    [Range(1, 1000)]
    public int MonthlyReservationLimit { get; init; }

    public static async Task<EventOrNone> HandleAsync(
        UpdateUserMonthlyReservationLimit command,
        ICommandContext context)
    {
        if (command.UserId == Guid.Empty)
        {
            throw new ApplicationException("UserId is required.");
        }

        if (command.MonthlyReservationLimit <= 0)
        {
            throw new ApplicationException("Monthly reservation limit must be greater than zero.");
        }

        var tag = new UserTag(command.UserId);
        if (!await context.TagExistsAsync(tag))
        {
            throw new ApplicationException($"User {command.UserId} is not registered.");
        }

        var state = await context.GetStateAsync<UserDirectoryProjector>(tag);
        if (state.Payload is not UserDirectoryState.UserDirectoryActive active)
        {
            throw new ApplicationException("User is not active.");
        }

        return new UserProfileUpdated(
            active.UserId,
            active.DisplayName,
            active.Email,
            active.Department,
            command.MonthlyReservationLimit)
            .GetEventWithTags();
    }
}
