using Dcb.MeetingRoomModels.Events.Room;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.Room;

public record CreateRoom : ICommandWithHandler<CreateRoom>
{
    public Guid RoomId { get; init; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Range(1, 1000)]
    public int Capacity { get; init; }

    [Required]
    [StringLength(200)]
    public string Location { get; init; } = string.Empty;

    public List<string> Equipment { get; init; } = [];

    public bool RequiresApproval { get; init; }

    public static async Task<EventOrNone> HandleAsync(
        CreateRoom command,
        ICommandContext context)
    {
        var roomId = command.RoomId != Guid.Empty ? command.RoomId : Guid.CreateVersion7();
        var tag = new RoomTag(roomId);

        var exists = await context.TagExistsAsync(tag);
        if (exists)
        {
            throw new ApplicationException($"Room {roomId} already exists");
        }

        return new RoomCreated(
            roomId,
            command.Name,
            command.Capacity,
            command.Location,
            command.Equipment,
            command.RequiresApproval)
            .GetEventWithTags();
    }
}
