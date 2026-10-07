using Dcb.EventSource.MeetingRoom.Room;
using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Room;
using NUnit.Framework;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;

namespace SekibanDcbOrleans.Unit;

public class RoomStateImmutabilityTests
{
    private static Event Wrap(IEventPayload payload) => new(payload,
        SortableUniqueId.GenerateNew(), payload.GetType().Name, Guid.NewGuid(),
        new EventMetadata("", "", ""), []);

    [Test]
    public void RoomReservations_FoldPreservesNestedInputBuckets()
    {
        var id = Guid.NewGuid();
        var start = new DateTime(2026, 1, 1, 23, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(2);
        var input = RoomReservationsState.Empty.AddOrUpdateReservation(id, start, end,
            "Original", Guid.NewGuid(), ReservationSlotStatus.Held);
        var ev = new ReservationConfirmed(id, Guid.NewGuid(), Guid.NewGuid(),
            start.AddDays(1), end.AddDays(1), "Updated", start, null);
        var output = (RoomReservationsState)RoomReservationsProjector.Project(input, Wrap(ev));
        Assert.That(input.ActiveReservations[id].Purpose, Is.EqualTo("Original"));
        Assert.That(input.ActiveReservationsByDay.Count, Is.EqualTo(2));
        foreach (var bucket in input.ActiveReservationsByDay.Values)
            Assert.That(bucket[id].Purpose, Is.EqualTo("Original"));
        Assert.That(output.ActiveReservations[id].Purpose, Is.EqualTo("Updated"));
        Assert.That(output.HasConflict(ev.StartTime, ev.EndTime), Is.True);
        var removed = (RoomReservationsState)RoomReservationsProjector.Project(output,
            Wrap(new ReservationCancelled(id, ev.RoomId, ev.StartTime, ev.EndTime, "Cancel", start)));
        Assert.That(removed.ActiveReservationsByDay, Is.Empty);
        Assert.That(output.ActiveReservationsByDay.Count, Is.EqualTo(2));
    }

    [Test]
    public void RoomDailyActivity_FoldDoesNotChangeInput()
    {
        var id = Guid.NewGuid();
        var start = DateTime.UtcNow;
        var input = RoomDailyActivityState.Empty.AddReservation(id, start, start.AddHours(1), "Original", Guid.NewGuid());
        var ev = new ReservationConfirmed(id, Guid.NewGuid(), Guid.NewGuid(), start,
            start.AddHours(2), "Updated", start, null);
        var output = (RoomDailyActivityState)RoomDailyActivityProjector.Project(input, Wrap(ev));
        Assert.That(input.ConfirmedReservations[id].Purpose, Is.EqualTo("Original"));
        Assert.That(output.ConfirmedReservations[id].Purpose, Is.EqualTo("Updated"));
        var removed = output.RemoveReservation(id);
        Assert.That(removed.ConfirmedReservations, Is.Empty);
        Assert.That(output.ConfirmedReservations.ContainsKey(id), Is.True);
    }
}
