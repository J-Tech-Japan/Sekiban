using Dcb.MeetingRoomModels.Events.EquipmentReservation;
using Dcb.MeetingRoomModels.States.EquipmentReservation;
using Dcb.MeetingRoomModels.States.EquipmentReservation.Deciders;

namespace SekibanDcbOrleans.MeetingRoomModels.Unit.States;

public class EquipmentReservationDeciderTests
{
    private readonly Guid _reservationId = Guid.NewGuid();
    private readonly Guid _typeId = Guid.NewGuid();
    private readonly Guid _requesterId = Guid.NewGuid();
    private readonly Guid _itemId1 = Guid.NewGuid();
    private readonly Guid _itemId2 = Guid.NewGuid();
    private readonly DateTime _startTime = DateTime.UtcNow.AddHours(1);
    private readonly DateTime _endTime = DateTime.UtcNow.AddHours(3);

    [Fact]
    public void EquipmentReservationState_Empty_Should_Be_EquipmentReservationEmpty()
    {
        var empty = EquipmentReservationState.Empty;
        Assert.IsType<EquipmentReservationState.EquipmentReservationEmpty>(empty);
    }

    [Fact]
    public void EquipmentReservationCreatedDecider_Should_Create_Pending()
    {
        var ev = new EquipmentReservationCreated(
            _reservationId, _typeId, 2, null, _requesterId, _startTime, _endTime);

        var state = EquipmentReservationCreatedDecider.Create(ev);

        Assert.IsType<EquipmentReservationState.EquipmentReservationPending>(state);
        Assert.Equal(_reservationId, state.EquipmentReservationId);
        Assert.Equal(_typeId, state.EquipmentTypeId);
        Assert.Equal(2, state.Quantity);
        Assert.Null(state.RoomReservationId);
    }

    [Fact]
    public void EquipmentReservationCreatedDecider_Evolve_From_Empty_Should_Create_Pending()
    {
        var empty = EquipmentReservationState.Empty;
        var ev = new EquipmentReservationCreated(
            _reservationId, _typeId, 2, null, _requesterId, _startTime, _endTime);

        var state = empty.Evolve(ev);

        Assert.IsType<EquipmentReservationState.EquipmentReservationPending>(state);
    }

    [Fact]
    public void EquipmentItemAssignedDecider_Should_Transition_Pending_To_Assigned()
    {
        var pending = new EquipmentReservationState.EquipmentReservationPending(
            _reservationId, _typeId, 2, null, _requesterId, _startTime, _endTime);
        var ev = new EquipmentItemAssigned(_reservationId, _itemId1, DateTime.UtcNow);

        var state = pending.Evolve(ev);

        var assigned = Assert.IsType<EquipmentReservationState.EquipmentReservationAssigned>(state);
        Assert.Single(assigned.AssignedItemIds);
        Assert.Contains(_itemId1, assigned.AssignedItemIds);
    }

    [Fact]
    public void EquipmentItemAssignedDecider_Should_Add_Item_To_Assigned()
    {
        var assigned = new EquipmentReservationState.EquipmentReservationAssigned(
            _reservationId, _typeId, 2, null, _requesterId, _startTime, _endTime, [_itemId1]);
        var ev = new EquipmentItemAssigned(_reservationId, _itemId2, DateTime.UtcNow);

        var state = assigned.Evolve(ev);

        var updated = Assert.IsType<EquipmentReservationState.EquipmentReservationAssigned>(state);
        Assert.Equal(2, updated.AssignedItemIds.Count);
        Assert.Contains(_itemId1, updated.AssignedItemIds);
        Assert.Contains(_itemId2, updated.AssignedItemIds);
    }

    [Fact]
    public void EquipmentItemAssignedDecider_Validate_Should_Throw_For_Duplicate()
    {
        var assigned = new EquipmentReservationState.EquipmentReservationAssigned(
            _reservationId, _typeId, 2, null, _requesterId, _startTime, _endTime, [_itemId1]);

        var ex = Assert.Throws<InvalidOperationException>(() => assigned.Validate(_itemId1));
        Assert.Contains("already assigned", ex.Message);
    }

    [Fact]
    public void EquipmentReservationCheckedOutDecider_Should_Transition_To_CheckedOut()
    {
        var assigned = new EquipmentReservationState.EquipmentReservationAssigned(
            _reservationId, _typeId, 2, null, _requesterId, _startTime, _endTime, [_itemId1, _itemId2]);
        var checkoutTime = DateTime.UtcNow;
        var ev = new EquipmentCheckedOut(_reservationId, _itemId1, _requesterId, checkoutTime);

        var state = assigned.Evolve(ev);

        var checkedOut = Assert.IsType<EquipmentReservationState.EquipmentReservationCheckedOut>(state);
        Assert.Equal(_reservationId, checkedOut.EquipmentReservationId);
        Assert.Equal(2, checkedOut.CheckedOutItemIds.Count);
        Assert.Equal(_requesterId, checkedOut.CheckedOutBy);
    }

    [Fact]
    public void EquipmentReservationReturnedDecider_Should_Transition_To_Returned()
    {
        var checkedOut = new EquipmentReservationState.EquipmentReservationCheckedOut(
            _reservationId, _typeId, null, _requesterId, _startTime, _endTime,
            [_itemId1], _requesterId, DateTime.UtcNow.AddHours(-1));
        var returnTime = DateTime.UtcNow;
        var ev = new EquipmentReturned(_reservationId, _itemId1, _requesterId, returnTime, "Good");

        var state = checkedOut.Evolve(ev);

        var returned = Assert.IsType<EquipmentReservationState.EquipmentReservationReturned>(state);
        Assert.Equal(_reservationId, returned.EquipmentReservationId);
        Assert.Equal(returnTime, returned.ReturnedAt);
    }

    [Fact]
    public void EquipmentReservationCancelledDecider_Should_Cancel_Pending()
    {
        var pending = new EquipmentReservationState.EquipmentReservationPending(
            _reservationId, _typeId, 2, null, _requesterId, _startTime, _endTime);
        var cancelTime = DateTime.UtcNow;
        var ev = new EquipmentReservationCancelled(_reservationId, "No longer needed", cancelTime);

        var state = pending.Evolve(ev);

        var cancelled = Assert.IsType<EquipmentReservationState.EquipmentReservationCancelled>(state);
        Assert.Equal("No longer needed", cancelled.Reason);
    }

    [Fact]
    public void EquipmentReservationCancelledDecider_Should_Cancel_Assigned()
    {
        var assigned = new EquipmentReservationState.EquipmentReservationAssigned(
            _reservationId, _typeId, 2, null, _requesterId, _startTime, _endTime, [_itemId1]);
        var ev = new EquipmentReservationCancelled(_reservationId, "Changed plans", DateTime.UtcNow);

        var state = assigned.Evolve(ev);

        Assert.IsType<EquipmentReservationState.EquipmentReservationCancelled>(state);
    }

    [Fact]
    public void EquipmentReservationCancelledDecider_Should_Not_Cancel_CheckedOut()
    {
        var checkedOut = new EquipmentReservationState.EquipmentReservationCheckedOut(
            _reservationId, _typeId, null, _requesterId, _startTime, _endTime,
            [_itemId1], _requesterId, DateTime.UtcNow);
        var ev = new EquipmentReservationCancelled(_reservationId, "Try to cancel", DateTime.UtcNow);

        var state = checkedOut.Evolve(ev);

        Assert.Same(checkedOut, state); // Should not change
    }

    [Fact]
    public void EquipmentReservationCancelledDecider_Validate_Should_Throw_For_CheckedOut()
    {
        var checkedOut = new EquipmentReservationState.EquipmentReservationCheckedOut(
            _reservationId, _typeId, null, _requesterId, _startTime, _endTime,
            [_itemId1], _requesterId, DateTime.UtcNow);

        var ex = Assert.Throws<InvalidOperationException>(() => checkedOut.Validate());
        Assert.Contains("Return items first", ex.Message);
    }

    [Fact]
    public void Full_EquipmentReservation_Lifecycle_Complete()
    {
        EquipmentReservationState state = EquipmentReservationState.Empty;
        Assert.IsType<EquipmentReservationState.EquipmentReservationEmpty>(state);

        // Create reservation
        state = state.Evolve(new EquipmentReservationCreated(
            _reservationId, _typeId, 2, null, _requesterId, _startTime, _endTime));
        Assert.IsType<EquipmentReservationState.EquipmentReservationPending>(state);

        // Assign first item
        state = state.Evolve(new EquipmentItemAssigned(_reservationId, _itemId1, DateTime.UtcNow));
        Assert.IsType<EquipmentReservationState.EquipmentReservationAssigned>(state);

        // Assign second item
        state = state.Evolve(new EquipmentItemAssigned(_reservationId, _itemId2, DateTime.UtcNow));
        var assigned = Assert.IsType<EquipmentReservationState.EquipmentReservationAssigned>(state);
        Assert.Equal(2, assigned.AssignedItemIds.Count);

        // Checkout
        state = state.Evolve(new EquipmentCheckedOut(_reservationId, _itemId1, _requesterId, DateTime.UtcNow));
        Assert.IsType<EquipmentReservationState.EquipmentReservationCheckedOut>(state);

        // Return
        state = state.Evolve(new EquipmentReturned(_reservationId, _itemId1, _requesterId, DateTime.UtcNow, "Good"));
        Assert.IsType<EquipmentReservationState.EquipmentReservationReturned>(state);
    }

    [Fact]
    public void Full_EquipmentReservation_Lifecycle_Cancelled()
    {
        EquipmentReservationState state = EquipmentReservationState.Empty;

        // Create reservation
        state = state.Evolve(new EquipmentReservationCreated(
            _reservationId, _typeId, 1, null, _requesterId, _startTime, _endTime));

        // Assign item
        state = state.Evolve(new EquipmentItemAssigned(_reservationId, _itemId1, DateTime.UtcNow));

        // Cancel
        state = state.Evolve(new EquipmentReservationCancelled(_reservationId, "Meeting cancelled", DateTime.UtcNow));
        Assert.IsType<EquipmentReservationState.EquipmentReservationCancelled>(state);
    }

    [Fact]
    public void EquipmentReservation_With_RoomReservation_Link()
    {
        var roomReservationId = Guid.NewGuid();
        EquipmentReservationState state = EquipmentReservationState.Empty;

        state = state.Evolve(new EquipmentReservationCreated(
            _reservationId, _typeId, 1, roomReservationId, _requesterId, _startTime, _endTime));

        var pending = Assert.IsType<EquipmentReservationState.EquipmentReservationPending>(state);
        Assert.Equal(roomReservationId, pending.RoomReservationId);
    }
}
