using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.States.Room;

public record RoomState(
    Guid RoomId,
    string Name,
    int Capacity,
    string Location,
    List<string> Equipment,
    bool RequiresApproval,
    bool IsActive = true) : ITagStatePayload
{
    // Parameterless constructor for JSON deserialization
    public RoomState() : this(Guid.Empty, string.Empty, 0, string.Empty, [], false, true) { }

    public static RoomState Empty => new(Guid.Empty, string.Empty, 0, string.Empty, [], false, true);
}
