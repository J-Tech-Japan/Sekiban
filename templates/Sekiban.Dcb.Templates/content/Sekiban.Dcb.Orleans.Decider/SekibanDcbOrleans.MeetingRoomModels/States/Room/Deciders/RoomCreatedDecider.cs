using Dcb.MeetingRoomModels.Events.Room;
namespace Dcb.MeetingRoomModels.States.Room.Deciders;

/// <summary>
///     Decider for RoomCreated event
/// </summary>
public static class RoomCreatedDecider
{
    /// <summary>
    ///     Create a new RoomState from RoomCreated event
    /// </summary>
    public static RoomState Create(RoomCreated created) =>
        new(
            created.RoomId,
            created.Name,
            created.Capacity,
            created.Location,
            created.Equipment,
            true);
}
