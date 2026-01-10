using Sekiban.Dcb.Tags;
namespace Dcb.ImmutableModels.States.ClassRoom;

public record AvailableClassRoomState(Guid ClassRoomId, string Name, int MaxStudents, List<Guid> EnrolledStudentIds)
    : ITagStatePayload
{
    public int GetRemaining() => MaxStudents - EnrolledStudentIds.Count;

    public static AvailableClassRoomState Empty => new(Guid.Empty, string.Empty, 0, []);
}
