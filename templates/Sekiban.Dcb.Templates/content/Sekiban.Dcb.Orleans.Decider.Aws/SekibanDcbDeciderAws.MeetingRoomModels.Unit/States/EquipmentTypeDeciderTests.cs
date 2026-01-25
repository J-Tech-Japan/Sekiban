using Dcb.MeetingRoomModels.Events.EquipmentType;
using Dcb.MeetingRoomModels.States.EquipmentType;
using Dcb.MeetingRoomModels.States.EquipmentType.Deciders;

namespace SekibanDcbOrleans.MeetingRoomModels.Unit.States;

public class EquipmentTypeDeciderTests
{
    private readonly Guid _typeId = Guid.NewGuid();

    [Fact]
    public void EquipmentTypeState_Empty_Should_Be_EquipmentTypeEmpty()
    {
        var empty = EquipmentTypeState.Empty;
        Assert.IsType<EquipmentTypeState.EquipmentTypeEmpty>(empty);
    }

    [Fact]
    public void EquipmentTypeCreatedDecider_Should_Create_Active()
    {
        var ev = new EquipmentTypeCreated(_typeId, "Microphone", "Wireless microphone", 5, 2);

        var state = EquipmentTypeCreatedDecider.Create(ev);

        Assert.IsType<EquipmentTypeState.EquipmentTypeActive>(state);
        Assert.Equal(_typeId, state.EquipmentTypeId);
        Assert.Equal("Microphone", state.Name);
        Assert.Equal("Wireless microphone", state.Description);
        Assert.Equal(5, state.TotalQuantity);
        Assert.Equal(2, state.MaxPerReservation);
    }

    [Fact]
    public void EquipmentTypeCreatedDecider_Evolve_From_Empty_Should_Create_Active()
    {
        var empty = EquipmentTypeState.Empty;
        var ev = new EquipmentTypeCreated(_typeId, "Microphone", "Wireless microphone", 5, 2);

        var state = empty.Evolve(ev);

        Assert.IsType<EquipmentTypeState.EquipmentTypeActive>(state);
    }

    [Fact]
    public void EquipmentTypeUpdatedDecider_Should_Update_Active()
    {
        var active = new EquipmentTypeState.EquipmentTypeActive(
            _typeId, "Microphone", "Old desc", 5, 2);
        var ev = new EquipmentTypeUpdated(_typeId, "Updated Mic", "New desc", 3);

        var state = active.Evolve(ev);

        var updated = Assert.IsType<EquipmentTypeState.EquipmentTypeActive>(state);
        Assert.Equal("Updated Mic", updated.Name);
        Assert.Equal("New desc", updated.Description);
        Assert.Equal(3, updated.MaxPerReservation);
        Assert.Equal(5, updated.TotalQuantity); // TotalQuantity unchanged
    }

    [Fact]
    public void Full_EquipmentType_Lifecycle()
    {
        EquipmentTypeState state = EquipmentTypeState.Empty;
        Assert.IsType<EquipmentTypeState.EquipmentTypeEmpty>(state);

        // Create
        state = state.Evolve(new EquipmentTypeCreated(_typeId, "Microphone", "Wireless", 5, 2));
        Assert.IsType<EquipmentTypeState.EquipmentTypeActive>(state);

        // Update
        state = state.Evolve(new EquipmentTypeUpdated(_typeId, "Premium Mic", "High quality", 3));
        var active = Assert.IsType<EquipmentTypeState.EquipmentTypeActive>(state);
        Assert.Equal("Premium Mic", active.Name);
    }
}
