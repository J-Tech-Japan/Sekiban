using Dcb.MeetingRoomModels.Events.Room;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.Room;

public record ReactivateRoom : ICommandWithHandler<ReactivateRoom>
{
    [Required]
    public Guid RoomId { get; init; }

    [StringLength(500)]
    public string? Reason { get; init; }

    public static async Task<EventOrNone> HandleAsync(
        ReactivateRoom command,
        ICommandContext context)
    {
        var tag = new RoomTag(command.RoomId);
        var exists = await context.TagExistsAsync(tag);

        if (!exists)
        {
            throw new ApplicationException($"Room {command.RoomId} not found");
        }

        return new RoomReactivated(command.RoomId, command.Reason, DateTime.UtcNow)
            .GetEventWithTags();
    }
}
