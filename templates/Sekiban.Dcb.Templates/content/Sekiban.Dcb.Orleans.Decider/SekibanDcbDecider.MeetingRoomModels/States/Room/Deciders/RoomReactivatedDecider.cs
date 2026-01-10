using Dcb.MeetingRoomModels.Events.Room;
namespace Dcb.MeetingRoomModels.States.Room.Deciders;

/// <summary>
///     Decider for RoomReactivated event
/// </summary>
public static class RoomReactivatedDecider
{
    /// <summary>
    ///     Apply RoomReactivated event to RoomState
    /// </summary>
    public static RoomState Evolve(this RoomState state, RoomReactivated reactivated) =>
        state with { IsActive = true };
}
