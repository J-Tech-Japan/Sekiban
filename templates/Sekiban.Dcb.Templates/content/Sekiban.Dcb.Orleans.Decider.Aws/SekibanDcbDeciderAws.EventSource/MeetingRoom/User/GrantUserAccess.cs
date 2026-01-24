using Dcb.MeetingRoomModels.Events.UserAccess;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.User;

public record GrantUserAccess : ICommandWithHandler<GrantUserAccess>
{
    [Required]
    public Guid UserId { get; init; }

    [Required]
    [StringLength(100)]
    public string InitialRole { get; init; } = string.Empty;

    public static async Task<EventOrNone> HandleAsync(
        GrantUserAccess command,
        ICommandContext context)
    {
        if (command.UserId == Guid.Empty)
        {
            throw new ApplicationException("UserId is required.");
        }

        if (string.IsNullOrWhiteSpace(command.InitialRole))
        {
            throw new ApplicationException("Initial role is required.");
        }

        var tag = new UserAccessTag(command.UserId);
        if (await context.TagExistsAsync(tag))
        {
            throw new ApplicationException($"User access already exists for {command.UserId}.");
        }

        return new UserAccessGranted(command.UserId, command.InitialRole, DateTime.UtcNow).GetEventWithTags();
    }
}
