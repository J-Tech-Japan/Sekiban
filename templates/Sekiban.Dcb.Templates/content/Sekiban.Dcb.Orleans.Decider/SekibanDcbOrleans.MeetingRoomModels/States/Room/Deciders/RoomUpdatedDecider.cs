using Dcb.MeetingRoomModels.Events.Room;
namespace Dcb.MeetingRoomModels.States.Room.Deciders;

/// <summary>
///     Decider for RoomUpdated event
/// </summary>
public static class RoomUpdatedDecider
{
    /// <summary>
    ///     Apply RoomUpdated event to RoomState
    /// </summary>
    public static RoomState Evolve(this RoomState state, RoomUpdated updated) =>
        state with
        {
            Name = updated.Name,
            Capacity = updated.Capacity,
            Location = updated.Location,
            Equipment = updated.Equipment
        };
}
