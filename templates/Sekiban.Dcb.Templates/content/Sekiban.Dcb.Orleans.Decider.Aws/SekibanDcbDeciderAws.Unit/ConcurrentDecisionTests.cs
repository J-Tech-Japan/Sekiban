using Dcb.EventSource;
using Dcb.EventSource.MeetingRoom.ApprovalRequest;
using Dcb.EventSource.MeetingRoom.Reservation;
using Dcb.EventSource.MeetingRoom.Room;
using Dcb.EventSource.MeetingRoom.User;
using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.States.ApprovalRequest;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.Tags;
using NUnit.Framework;
using Sekiban.Dcb;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;

namespace SekibanDcbDeciderAws.Unit;

/// <summary>
///     The approval decision reads the approval request and the reservation in its command context. Those reads
///     are the command's consistency set: if another command writes to either tag after the reads and before
///     the decision is appended, the append must fail instead of recording a decision against a reservation
///     that has moved on.
/// </summary>
public class ConcurrentDecisionTests
{
    [Test]
    public async Task CancelCommittedBetweenReadsAndAppend_MakesTheDecisionFail()
    {
        var domain = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutorForTesting(domain, new InMemoryEventStore(domain.EventTypes));
        var roomId = Guid.CreateVersion7();
        var organizerId = Guid.CreateVersion7();
        var adminId = Guid.CreateVersion7();
        var reservationId = Guid.CreateVersion7();
        var approvalRequestId = Guid.CreateVersion7();

        await executor.ExecuteAsync(new CreateRoom
        {
            RoomId = roomId, Name = "Board", Location = "Floor 2", Capacity = 8, RequiresApproval = true
        });
        await executor.ExecuteAsync(new RegisterUser { UserId = organizerId, DisplayName = "Organizer", Email = "o@example.com" });
        await executor.ExecuteAsync(new GrantUserAccess { UserId = organizerId, InitialRole = "User" });
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 10, 0, 0, DateTimeKind.Utc).AddMonths(1).AddDays(2);
        await executor.ExecuteAsync(new CreateQuickReservation
        {
            ReservationId = reservationId, RoomId = roomId, ApprovalRequestId = approvalRequestId,
            OrganizerId = organizerId, OrganizerName = "Organizer", StartTime = start, EndTime = start.AddHours(1),
            Purpose = "Board"
        });

        var decision = new RecordApprovalDecision
        {
            ApprovalRequestId = approvalRequestId, ReservationId = reservationId, ApproverId = adminId,
            Decision = ApprovalDecision.Approved, Comment = "ok"
        };

        // Run the real decision handler, but let the organizer's cancel commit after the handler has read
        // its state and before it appends its first event.
        var cancelCommitted = false;
        Assert.That(
            async () => await executor.ExecuteAsync(
                decision,
                (command, context) => RecordApprovalDecision.HandleAsync(
                    command,
                    new InterleavingCommandContext(context, async () =>
                    {
                        await executor.ExecuteAsync(new CancelReservation
                        {
                            ReservationId = reservationId, Reason = "Plans changed", RequestedByUserId = organizerId
                        });
                        cancelCommitted = true;
                    }))),
            Throws.Exception.With.Message.Contains("has been modified"));

        Assert.That(cancelCommitted, Is.True);
        var reservation = await executor.GetTagStateAsync(
            new TagStateId(new ReservationTag(reservationId), nameof(ReservationProjector)));
        Assert.That(reservation.Payload, Is.InstanceOf<ReservationState.ReservationCancelled>());
        var approval = await executor.GetTagStateAsync(
            new TagStateId(new ApprovalRequestTag(approvalRequestId), nameof(ApprovalRequestProjector)));
        // Closed by the cancel, not approved by the decision that lost the race
        Assert.That(approval.Payload, Is.InstanceOf<ApprovalRequestState.ApprovalRequestWithdrawn>());
    }

    /// <summary>Forwards to the real context and runs <paramref name="beforeFirstAppend" /> once, just before the first append.</summary>
    private sealed class InterleavingCommandContext(ICommandContext inner, Func<Task> beforeFirstAppend) : ICommandContext
    {
        private bool _interleaved;

        public Task<TagStateTyped<TState>> GetStateAsync<TState, TProjector>(ITag tag)
            where TState : ITagStatePayload where TProjector : ITagProjector<TProjector> =>
            inner.GetStateAsync<TState, TProjector>(tag);

        public Task<TagState> GetStateAsync<TProjector>(ITag tag) where TProjector : ITagProjector<TProjector> =>
            inner.GetStateAsync<TProjector>(tag);

        public Task<bool> TagExistsAsync(ITag tag) => inner.TagExistsAsync(tag);

        public Task<string> GetTagLatestSortableUniqueIdAsync(ITag tag) => inner.GetTagLatestSortableUniqueIdAsync(tag);

        public async Task<EventOrNone> AppendEvent(IEventPayload ev, params ITag[] tags)
        {
            await InterleaveOnceAsync();
            return await inner.AppendEvent(ev, tags);
        }

        public async Task<EventOrNone> AppendEvent(EventPayloadWithTags eventPayloadWithTags)
        {
            await InterleaveOnceAsync();
            return await inner.AppendEvent(eventPayloadWithTags);
        }

        private async Task InterleaveOnceAsync()
        {
            if (_interleaved) return;
            _interleaved = true;
            await beforeFirstAppend();
        }
    }
}
