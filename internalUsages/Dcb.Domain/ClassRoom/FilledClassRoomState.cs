using Orleans;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.ClassRoom;

[GenerateSerializer]
public record FilledClassRoomState(Guid ClassRoomId, string Name, List<Guid> EnrolledStudentIds, bool IsFull)
    : ITagStatePayload
{
}
