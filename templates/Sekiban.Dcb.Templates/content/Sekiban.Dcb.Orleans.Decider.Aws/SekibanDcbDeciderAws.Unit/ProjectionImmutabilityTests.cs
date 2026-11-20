using Dcb.EventSource;
using Dcb.EventSource.MeetingRoom.Projections;
using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.Tags;
using NUnit.Framework;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;

namespace SekibanDcbOrleans.Unit;

public class ProjectionImmutabilityTests
{
    private static Event Wrap(IEventPayload payload) => new(payload,
        SortableUniqueId.GenerateNew(), payload.GetType().Name, Guid.NewGuid(),
        new EventMetadata("", "", ""), []);

    [Test]
    public void ReservationList_FoldDoesNotChangeInput()
    {
        var id = Guid.NewGuid();
        var input = ReservationListProjection.GenerateInitialPayload();
        var ev = new ReservationDraftCreated(id, Guid.NewGuid(), Guid.NewGuid(), "Alice",
            DateTime.UtcNow, DateTime.UtcNow.AddHours(1), "Meeting");
        var output = ReservationListProjection.Project(input, Wrap(ev), [new ReservationTag(id)],
            DomainType.GetDomainTypes(), new SortableUniqueId(SortableUniqueId.GenerateNew()));
        Assert.That(input.Reservations, Is.Empty);
        Assert.That(output, Is.Not.SameAs(input));
        Assert.That(output.Reservations.ContainsKey(id), Is.True);
    }

    [Test]
    public void ApprovalRequestList_FoldDoesNotChangeInput()
    {
        var id = Guid.NewGuid();
        var input = ApprovalRequestListProjection.GenerateInitialPayload();
        var ev = new ApprovalFlowStarted(id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            [Guid.NewGuid()], DateTime.UtcNow, "Please approve");
        var output = ApprovalRequestListProjection.Project(input, Wrap(ev), [new ApprovalRequestTag(id)],
            DomainType.GetDomainTypes(), new SortableUniqueId(SortableUniqueId.GenerateNew()));
        Assert.That(input.ApprovalRequests, Is.Empty);
        Assert.That(output, Is.Not.SameAs(input));
        Assert.That(output.ApprovalRequests.ContainsKey(id), Is.True);
    }
}
