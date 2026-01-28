using Orleans;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.WithoutResult.ClassRoom;

public record FilledClassRoomState(Guid ClassRoomId, string Name, List<Guid> EnrolledStudentIds, bool IsFull)
    : ITagStatePayload
{
}
