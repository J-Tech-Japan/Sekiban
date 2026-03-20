using Dcb.EventSource;
using Dcb.EventSource.MeetingRoom.Reservation;
using Dcb.EventSource.MeetingRoom.Room;
using Dcb.EventSource.MeetingRoom.User;
using Dcb.Interactions.Workflows.Reservation;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Tags;

namespace SekibanDcbOrleans.Interactions.Unit.Workflows.Reservation;

public class CreateAndHoldReservationWorkflowTests
{
    private readonly ISekibanExecutor _executor = new InMemoryDcbExecutor(DomainType.GetDomainTypes());

    private async Task<(Guid RoomId, Guid UserId)> SetupRoomAndUserAsync(bool requiresApproval = false)
    {
        var roomId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        await _executor.ExecuteAsync(new CreateRoom
        {
            RoomId = roomId,
            Name = "Conference Room A",
            Capacity = 10,
            Location = "Floor 1",
            Equipment = ["Projector", "Whiteboard"],
            RequiresApproval = requiresApproval
        });

        await _executor.ExecuteAsync(new RegisterUser
        {
            UserId = userId,
            DisplayName = "Test User",
            Email = "test@example.com",
            Department = "Engineering",
            MonthlyReservationLimit = 10
        });

        await _executor.ExecuteAsync(new GrantUserAccess
        {
            UserId = userId,
            InitialRole = "Admin"
        });

        return (roomId, userId);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesReservationInHeldState()
    {
        var (roomId, userId) = await SetupRoomAndUserAsync();
        var startTime = DateTime.UtcNow.AddHours(1);
        var endTime = DateTime.UtcNow.AddHours(2);

        var workflow = new CreateAndHoldReservationWorkflow(_executor);
        var reservationId = await workflow.ExecuteAsync(
            roomId, userId, "Test User", startTime, endTime, "Team meeting");

        Assert.NotEqual(Guid.Empty, reservationId);

        TagState tagState = await _executor
            .GetTagStateAsync<ReservationProjector>(new ReservationTag(reservationId));
        Assert.IsType<ReservationState.ReservationHeld>(tagState.Payload);
    }

    [Fact]
    public async Task ExecuteAsync_HeldState_ContainsCorrectDetails()
    {
        var (roomId, userId) = await SetupRoomAndUserAsync();
        var startTime = DateTime.UtcNow.AddHours(1);
        var endTime = DateTime.UtcNow.AddHours(2);

        var workflow = new CreateAndHoldReservationWorkflow(_executor);
        var reservationId = await workflow.ExecuteAsync(
            roomId, userId, "Test User", startTime, endTime, "Team meeting",
            selectedEquipment: ["Projector"]);

        TagState tagState = await _executor
            .GetTagStateAsync<ReservationProjector>(new ReservationTag(reservationId));
        var held = Assert.IsType<ReservationState.ReservationHeld>(tagState.Payload);

        Assert.Equal(roomId, held.RoomId);
        Assert.Equal(userId, held.OrganizerId);
        Assert.Equal("Test User", held.OrganizerName);
        Assert.Equal("Team meeting", held.Purpose);
        Assert.Contains("Projector", held.SelectedEquipment);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistentRoom_ThrowsException()
    {
        var userId = Guid.CreateVersion7();
        var workflow = new CreateAndHoldReservationWorkflow(_executor);

        await Assert.ThrowsAsync<ApplicationException>(async () =>
            await workflow.ExecuteAsync(
                Guid.CreateVersion7(), userId, "User", DateTime.UtcNow.AddHours(1),
                DateTime.UtcNow.AddHours(2), "Meeting"));
    }
}
