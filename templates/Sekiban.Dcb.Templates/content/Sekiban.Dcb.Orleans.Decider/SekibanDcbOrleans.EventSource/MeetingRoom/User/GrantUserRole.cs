using Dcb.MeetingRoomModels.Events.UserAccess;
using Dcb.MeetingRoomModels.States.UserAccess;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.User;

public record GrantUserRole : ICommandWithHandler<GrantUserRole>
{
    [Required]
    public Guid UserId { get; init; }

    [Required]
    [StringLength(100)]
    public string Role { get; init; } = string.Empty;

    public static async Task<EventOrNone> HandleAsync(
        GrantUserRole command,
        ICommandContext context)
    {
        if (command.UserId == Guid.Empty)
        {
            throw new ApplicationException("UserId is required.");
        }

        if (string.IsNullOrWhiteSpace(command.Role))
        {
            throw new ApplicationException("Role is required.");
        }

        var tag = new UserAccessTag(command.UserId);
        if (!await context.TagExistsAsync(tag))
        {
            throw new ApplicationException($"User access not found for {command.UserId}.");
        }

        var state = await context.GetStateAsync<UserAccessProjector>(tag);
        if (state.Payload is not UserAccessState.UserAccessActive active)
        {
            throw new ApplicationException("User access is not active.");
        }

        if (active.HasRole(command.Role))
        {
            return EventOrNone.Empty;
        }

        return new UserRoleGranted(command.UserId, command.Role, DateTime.UtcNow).GetEventWithTags();
    }
}
