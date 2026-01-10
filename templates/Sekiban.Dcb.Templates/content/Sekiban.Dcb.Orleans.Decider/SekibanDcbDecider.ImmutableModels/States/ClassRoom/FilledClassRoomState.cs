using Sekiban.Dcb.Tags;
namespace Dcb.ImmutableModels.States.ClassRoom;

public record FilledClassRoomState(Guid ClassRoomId, string Name, List<Guid> EnrolledStudentIds, bool IsFull)
    : ITagStatePayload
{
    public static FilledClassRoomState Empty => new(Guid.Empty, string.Empty, [], false);
}
