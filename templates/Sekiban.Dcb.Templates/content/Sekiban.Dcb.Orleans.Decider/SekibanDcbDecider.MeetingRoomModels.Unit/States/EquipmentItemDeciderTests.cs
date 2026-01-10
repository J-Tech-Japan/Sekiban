using Dcb.MeetingRoomModels.Events.EquipmentItem;
using Dcb.MeetingRoomModels.Events.EquipmentReservation;
using Dcb.MeetingRoomModels.States.EquipmentItem;
using Dcb.MeetingRoomModels.States.EquipmentItem.Deciders;

namespace SekibanDcbOrleans.MeetingRoomModels.Unit.States;

public class EquipmentItemDeciderTests
{
    private readonly Guid _itemId = Guid.NewGuid();
    private readonly Guid _typeId = Guid.NewGuid();
    private readonly Guid _reservationId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateTime _registeredAt = DateTime.UtcNow.AddDays(-30);

    [Fact]
    public void EquipmentItemState_Empty_Should_Be_EquipmentItemEmpty()
    {
        var empty = EquipmentItemState.Empty;
        Assert.IsType<EquipmentItemState.EquipmentItemEmpty>(empty);
    }

    [Fact]
    public void EquipmentItemRegisteredDecider_Should_Create_Available()
    {
        var ev = new EquipmentItemRegistered(_itemId, _typeId, "MIC-001", "New", _registeredAt);

        var state = EquipmentItemRegisteredDecider.Create(ev);

        Assert.IsType<EquipmentItemState.EquipmentItemAvailable>(state);
        Assert.Equal(_itemId, state.EquipmentItemId);
        Assert.Equal(_typeId, state.EquipmentTypeId);
        Assert.Equal("MIC-001", state.SerialNumber);
    }

    [Fact]
    public void EquipmentItemRegisteredDecider_Evolve_From_Empty_Should_Create_Available()
    {
        var empty = EquipmentItemState.Empty;
        var ev = new EquipmentItemRegistered(_itemId, _typeId, "MIC-001", null, _registeredAt);

        var state = empty.Evolve(ev);

        Assert.IsType<EquipmentItemState.EquipmentItemAvailable>(state);
    }

    [Fact]
    public void EquipmentItemCheckedOutDecider_Should_Transition_To_CheckedOut()
    {
        var available = new EquipmentItemState.EquipmentItemAvailable(
            _itemId, _typeId, "MIC-001", null, _registeredAt);
        var checkoutTime = DateTime.UtcNow;
        var ev = new EquipmentCheckedOut(_reservationId, _itemId, _userId, checkoutTime);

        var state = available.Evolve(ev);

        var checkedOut = Assert.IsType<EquipmentItemState.EquipmentItemCheckedOut>(state);
        Assert.Equal(_itemId, checkedOut.EquipmentItemId);
        Assert.Equal(_reservationId, checkedOut.EquipmentReservationId);
        Assert.Equal(_userId, checkedOut.CheckedOutBy);
        Assert.Equal(checkoutTime, checkedOut.CheckedOutAt);
    }

    [Fact]
    public void EquipmentItemReturnedDecider_Should_Transition_Back_To_Available()
    {
        var checkedOut = new EquipmentItemState.EquipmentItemCheckedOut(
            _itemId, _typeId, "MIC-001", null, _registeredAt, _reservationId, _userId, DateTime.UtcNow.AddHours(-2));
        var returnTime = DateTime.UtcNow;
        var ev = new EquipmentReturned(_reservationId, _itemId, _userId, returnTime, "Good condition");

        var state = checkedOut.Evolve(ev);

        var available = Assert.IsType<EquipmentItemState.EquipmentItemAvailable>(state);
        Assert.Equal(_itemId, available.EquipmentItemId);
        Assert.Equal("MIC-001", available.SerialNumber);
    }

    [Fact]
    public void EquipmentItemRetiredDecider_Should_Transition_To_Retired()
    {
        var available = new EquipmentItemState.EquipmentItemAvailable(
            _itemId, _typeId, "MIC-001", null, _registeredAt);
        var retireTime = DateTime.UtcNow;
        var ev = new EquipmentItemRetired(_itemId, _typeId, "Broken beyond repair", retireTime);

        var state = available.Evolve(ev);

        var retired = Assert.IsType<EquipmentItemState.EquipmentItemRetired>(state);
        Assert.Equal(_itemId, retired.EquipmentItemId);
        Assert.Equal("Broken beyond repair", retired.Reason);
        Assert.Equal(retireTime, retired.RetiredAt);
    }

    [Fact]
    public void EquipmentItemRetiredDecider_Should_Not_Retire_CheckedOut_Item()
    {
        var checkedOut = new EquipmentItemState.EquipmentItemCheckedOut(
            _itemId, _typeId, "MIC-001", null, _registeredAt, _reservationId, _userId, DateTime.UtcNow);
        var ev = new EquipmentItemRetired(_itemId, _typeId, "Try to retire", DateTime.UtcNow);

        var state = checkedOut.Evolve(ev);

        Assert.Same(checkedOut, state); // Should not change state
    }

    [Fact]
    public void EquipmentItemReturnedDecider_Validate_Should_Throw_For_Wrong_Reservation()
    {
        var wrongReservationId = Guid.NewGuid();
        var checkedOut = new EquipmentItemState.EquipmentItemCheckedOut(
            _itemId, _typeId, "MIC-001", null, _registeredAt, _reservationId, _userId, DateTime.UtcNow);

        var ex = Assert.Throws<InvalidOperationException>(() => checkedOut.Validate(wrongReservationId));
        Assert.Contains("not", ex.Message);
    }

    [Fact]
    public void Full_EquipmentItem_Lifecycle()
    {
        EquipmentItemState state = EquipmentItemState.Empty;
        Assert.IsType<EquipmentItemState.EquipmentItemEmpty>(state);

        // Register
        state = state.Evolve(new EquipmentItemRegistered(_itemId, _typeId, "MIC-001", null, _registeredAt));
        Assert.IsType<EquipmentItemState.EquipmentItemAvailable>(state);

        // Checkout
        state = state.Evolve(new EquipmentCheckedOut(_reservationId, _itemId, _userId, DateTime.UtcNow));
        Assert.IsType<EquipmentItemState.EquipmentItemCheckedOut>(state);

        // Return
        state = state.Evolve(new EquipmentReturned(_reservationId, _itemId, _userId, DateTime.UtcNow, "OK"));
        Assert.IsType<EquipmentItemState.EquipmentItemAvailable>(state);

        // Checkout again
        var reservation2 = Guid.NewGuid();
        state = state.Evolve(new EquipmentCheckedOut(reservation2, _itemId, _userId, DateTime.UtcNow));
        Assert.IsType<EquipmentItemState.EquipmentItemCheckedOut>(state);

        // Return again
        state = state.Evolve(new EquipmentReturned(reservation2, _itemId, _userId, DateTime.UtcNow, "OK"));
        Assert.IsType<EquipmentItemState.EquipmentItemAvailable>(state);

        // Retire
        state = state.Evolve(new EquipmentItemRetired(_itemId, _typeId, "End of life", DateTime.UtcNow));
        Assert.IsType<EquipmentItemState.EquipmentItemRetired>(state);
    }
}
