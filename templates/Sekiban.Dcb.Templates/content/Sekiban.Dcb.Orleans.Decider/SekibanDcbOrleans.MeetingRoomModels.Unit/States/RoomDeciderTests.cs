using Dcb.MeetingRoomModels.Events.Room;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.States.Room.Deciders;

namespace SekibanDcbOrleans.MeetingRoomModels.Unit.States;

public class RoomDeciderTests
{
    [Fact]
    public void RoomState_Empty_Should_Be_Default()
    {
        var empty = RoomState.Empty;

        Assert.Equal(Guid.Empty, empty.RoomId);
        Assert.Equal(string.Empty, empty.Name);
        Assert.Equal(0, empty.Capacity);
        Assert.Equal(string.Empty, empty.Location);
        Assert.Empty(empty.Equipment);
    }

    [Fact]
    public void RoomCreatedDecider_Should_Create_State()
    {
        var roomId = Guid.NewGuid();
        var ev = new RoomCreated(roomId, "Conference Room A", 10, "Building 1", ["Projector", "Whiteboard"]);

        var state = RoomCreatedDecider.Create(ev);

        Assert.Equal(roomId, state.RoomId);
        Assert.Equal("Conference Room A", state.Name);
        Assert.Equal(10, state.Capacity);
        Assert.Equal("Building 1", state.Location);
        Assert.Equal(2, state.Equipment.Count);
        Assert.Contains("Projector", state.Equipment);
        Assert.Contains("Whiteboard", state.Equipment);
    }

    [Fact]
    public void RoomUpdatedDecider_Should_Update_State()
    {
        var roomId = Guid.NewGuid();
        var state = new RoomState(roomId, "Old Name", 5, "Old Location", ["Old Equipment"]);
        var ev = new RoomUpdated(roomId, "New Name", 15, "New Location", ["New Equipment"]);

        var newState = state.Evolve(ev);

        Assert.Equal(roomId, newState.RoomId);
        Assert.Equal("New Name", newState.Name);
        Assert.Equal(15, newState.Capacity);
        Assert.Equal("New Location", newState.Location);
        Assert.Single(newState.Equipment);
        Assert.Contains("New Equipment", newState.Equipment);
    }
}
