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

public class QuickReservationWorkflowTests
{
    private readonly ISekibanExecutor _executor = new InMemoryDcbExecutor(DomainType.GetDomainTypes());

    private async Task<(Guid RoomId, Guid UserId)> SetupRoomAndUserAsync(bool requiresApproval = false)
    {
        var roomId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        await _executor.ExecuteAsync(new CreateRoom
        {
            RoomId = roomId,
            Name = "Conference Room B",
            Capacity = 20,
            Location = "Floor 2",
            Equipment = ["Projector", "TV"],
            RequiresApproval = requiresApproval
        });

        await _executor.ExecuteAsync(new RegisterUser
        {
            UserId = userId,
            DisplayName = "Quick User",
            Email = "quick@example.com",
            Department = "Sales",
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
    public async Task ExecuteAsync_NoApproval_ConfirmsReservation()
    {
        var (roomId, userId) = await SetupRoomAndUserAsync(requiresApproval: false);
        var startTime = DateTime.UtcNow.AddHours(1);
        var endTime = DateTime.UtcNow.AddHours(2);

        var workflow = new QuickReservationWorkflow(_executor);
        var result = await workflow.ExecuteAsync(
            roomId, userId, "Quick User", startTime, endTime, "Quick meeting");

        Assert.NotEqual(Guid.Empty, result.ReservationId);
        Assert.False(result.RequiresApproval);
        Assert.Null(result.ApprovalRequestId);

        TagState tagState = await _executor
            .GetTagStateAsync<ReservationProjector>(new ReservationTag(result.ReservationId));
        Assert.IsType<ReservationState.ReservationConfirmed>(tagState.Payload);
    }

    [Fact]
    public async Task ExecuteAsync_WithApproval_HoldsReservationPendingApproval()
    {
        var (roomId, userId) = await SetupRoomAndUserAsync(requiresApproval: true);
        var startTime = DateTime.UtcNow.AddHours(1);
        var endTime = DateTime.UtcNow.AddHours(2);

        var workflow = new QuickReservationWorkflow(_executor);
        var result = await workflow.ExecuteAsync(
            roomId, userId, "Quick User", startTime, endTime, "Approval meeting",
            approvalRequestComment: "Please approve");

        Assert.True(result.RequiresApproval);
        Assert.NotNull(result.ApprovalRequestId);

        TagState tagState = await _executor
            .GetTagStateAsync<ReservationProjector>(new ReservationTag(result.ReservationId));
        Assert.IsType<ReservationState.ReservationHeld>(tagState.Payload);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNonEmptySortableUniqueId()
    {
        var (roomId, userId) = await SetupRoomAndUserAsync();
        var startTime = DateTime.UtcNow.AddHours(1);
        var endTime = DateTime.UtcNow.AddHours(2);

        var workflow = new QuickReservationWorkflow(_executor);
        var result = await workflow.ExecuteAsync(
            roomId, userId, "Quick User", startTime, endTime, "ID test");

        Assert.NotEmpty(result.SortableUniqueId);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleReservations_NoConflict_Succeed()
    {
        var (roomId, userId) = await SetupRoomAndUserAsync();
        var workflow = new QuickReservationWorkflow(_executor);

        var result1 = await workflow.ExecuteAsync(
            roomId, userId, "Quick User",
            DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2), "Meeting 1");

        var result2 = await workflow.ExecuteAsync(
            roomId, userId, "Quick User",
            DateTime.UtcNow.AddHours(3), DateTime.UtcNow.AddHours(4), "Meeting 2");

        Assert.NotEqual(result1.ReservationId, result2.ReservationId);

        TagState state1 = await _executor
            .GetTagStateAsync<ReservationProjector>(new ReservationTag(result1.ReservationId));
        TagState state2 = await _executor
            .GetTagStateAsync<ReservationProjector>(new ReservationTag(result2.ReservationId));

        Assert.IsType<ReservationState.ReservationConfirmed>(state1.Payload);
        Assert.IsType<ReservationState.ReservationConfirmed>(state2.Payload);
    }
}
