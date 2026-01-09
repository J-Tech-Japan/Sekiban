using Dcb.MeetingRoomModels.Events.UserDirectory;
using Dcb.MeetingRoomModels.States.UserDirectory;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.User;

public record RegisterUser : ICommandWithHandler<RegisterUser>
{
    [Required]
    public Guid UserId { get; init; }

    [Required]
    [StringLength(200)]
    public string DisplayName { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [StringLength(200)]
    public string? Department { get; init; }

    [Range(1, 1000)]
    public int MonthlyReservationLimit { get; init; } = UserDirectoryState.DefaultMonthlyReservationLimit;

    public static async Task<EventOrNone> HandleAsync(
        RegisterUser command,
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
        if (await context.TagExistsAsync(tag))
        {
            throw new ApplicationException($"User {command.UserId} is already registered.");
        }

        return new UserRegistered(
            command.UserId,
            command.DisplayName,
            command.Email,
            command.Department,
            DateTime.UtcNow,
            command.MonthlyReservationLimit)
            .GetEventWithTags();
    }
}
