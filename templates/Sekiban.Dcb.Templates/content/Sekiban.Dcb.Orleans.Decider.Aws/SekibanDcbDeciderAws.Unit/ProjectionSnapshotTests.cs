using System.Text.Json;
using Dcb.EventSource;
using Dcb.EventSource.MeetingRoom.Projections;
using Dcb.EventSource.Projections;
using Dcb.ImmutableModels.Events.ClassRoom;
using Dcb.ImmutableModels.Events.Enrollment;
using Dcb.ImmutableModels.States.ClassRoom;
using Dcb.ImmutableModels.Tags;
using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.Events.Room;
using Dcb.MeetingRoomModels.Tags;
using NUnit.Framework;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace SekibanDcbDeciderAws.Unit;

public class ProjectionSnapshotTests
{
    [Test]
    public void ClassRoomListProjection_RoundTripsThroughJson()
    {
        var domain = DomainType.GetDomainTypes();
        var available = Guid.CreateVersion7();
        var filled = Guid.CreateVersion7();
        var student = Guid.CreateVersion7();

        var projection = ClassRoomListProjection.GenerateInitialPayload();
        projection = Apply(projection, new ClassRoomCreated(available, "Math", 3), new ClassRoomTag(available));
        projection = Apply(projection, new ClassRoomCreated(filled, "Art", 1), new ClassRoomTag(filled));
        projection = Apply(projection, new StudentEnrolledInClassRoom(student, filled), new ClassRoomTag(filled), new StudentTag(student));

        var json = JsonSerializer.Serialize(projection, domain.JsonSerializerOptions);
        var restored = JsonSerializer.Deserialize<ClassRoomListProjection>(json, domain.JsonSerializerOptions);

        Assert.That(restored, Is.Not.Null);
        var rooms = restored!.GetAllClassRooms();
        Assert.That(rooms, Has.Count.EqualTo(2));
        Assert.That(rooms.OfType<AvailableClassRoomState>().Single().ClassRoomId, Is.EqualTo(available));
        var restoredFilled = rooms.OfType<FilledClassRoomState>().Single();
        Assert.That(restoredFilled.ClassRoomId, Is.EqualTo(filled));
        Assert.That(restoredFilled.EnrolledStudentIds, Is.EqualTo(new[] { student }));
    }

    [Test]
    public void ClassRoomListProjection_MovesRoomBetweenAvailableAndFilled()
    {
        var classRoom = Guid.CreateVersion7();
        var student = Guid.CreateVersion7();
        var projection = Apply(ClassRoomListProjection.GenerateInitialPayload(), new ClassRoomCreated(classRoom, "Art", 1), new ClassRoomTag(classRoom));
        projection = Apply(projection, new StudentEnrolledInClassRoom(student, classRoom), new ClassRoomTag(classRoom));
        Assert.That(projection.FilledClassRooms.ContainsKey(classRoom) && !projection.AvailableClassRooms.ContainsKey(classRoom));

        projection = Apply(projection, new StudentDroppedFromClassRoom(student, classRoom), new ClassRoomTag(classRoom));
        Assert.That(projection.AvailableClassRooms.ContainsKey(classRoom) && !projection.FilledClassRooms.ContainsKey(classRoom));
        Assert.That(projection.GetAllClassRooms(), Has.Count.EqualTo(1));
    }

    [Test]
    public void RoomListProjection_IgnoresRoomTagsWithoutRoom()
    {
        var unknownRoom = Guid.CreateVersion7();
        var start = DateTime.UtcNow.AddDays(1);
        var ev = new ReservationDraftCreated(Guid.CreateVersion7(), unknownRoom, Guid.CreateVersion7(), "Organizer", start, start.AddHours(1), "Sync", []);
        var projection = ApplyRoom(RoomListProjection.GenerateInitialPayload(), ev, new RoomTag(unknownRoom));
        Assert.That(projection.Rooms, Is.Empty);
    }

    [Test]
    public void RoomListProjection_KeepsCreatedRoom()
    {
        var roomId = Guid.CreateVersion7();
        var projection = ApplyRoom(
            RoomListProjection.GenerateInitialPayload(),
            new RoomCreated(roomId, "Room A", 4, "Floor 1", [], false),
            new RoomTag(roomId));
        Assert.That(projection.Rooms.Keys, Is.EqualTo(new[] { roomId }));
    }

    private static ClassRoomListProjection Apply(ClassRoomListProjection projection, IEventPayload payload, params ITag[] tags) =>
        ClassRoomListProjection.Project(projection, ToEvent(payload, tags), [.. tags], DomainType.GetDomainTypes(), new SortableUniqueId(SortableUniqueId.GenerateNew()));

    private static RoomListProjection ApplyRoom(RoomListProjection projection, IEventPayload payload, params ITag[] tags) =>
        RoomListProjection.Project(projection, ToEvent(payload, tags), [.. tags], DomainType.GetDomainTypes(), new SortableUniqueId(SortableUniqueId.GenerateNew()));

    private static Event ToEvent(IEventPayload payload, ITag[] tags) =>
        new(payload,
            SortableUniqueId.GenerateNew(),
            payload.GetType().Name,
            Guid.CreateVersion7(),
            new EventMetadata("", "", ""),
            []);
}
