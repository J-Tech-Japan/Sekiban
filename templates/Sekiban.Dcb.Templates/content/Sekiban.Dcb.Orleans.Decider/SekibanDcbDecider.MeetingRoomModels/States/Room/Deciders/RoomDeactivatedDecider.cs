using Dcb.MeetingRoomModels.Events.Room;
namespace Dcb.MeetingRoomModels.States.Room.Deciders;

/// <summary>
///     Decider for RoomDeactivated event
/// </summary>
public static class RoomDeactivatedDecider
{
    /// <summary>
    ///     Apply RoomDeactivated event to RoomState
    /// </summary>
    public static RoomState Evolve(this RoomState state, RoomDeactivated deactivated) =>
        state with { IsActive = false };
}
