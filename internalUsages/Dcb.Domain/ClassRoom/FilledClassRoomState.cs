using Sekiban.Dcb.Tags;
namespace Dcb.Domain.ClassRoom;

public record FilledClassRoomState(
    Guid ClassRoomId,
    string Name,
    List<Guid> EnrolledStudentIds,
    bool IsFull
) : ITagStatePayload
{
}