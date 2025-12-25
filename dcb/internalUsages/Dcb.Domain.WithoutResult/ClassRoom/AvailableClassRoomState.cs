using Orleans;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.WithoutResult.ClassRoom;

[GenerateSerializer]
public record AvailableClassRoomState(Guid ClassRoomId, string Name, int MaxStudents, List<Guid> EnrolledStudentIds)
    : ITagStatePayload
{
    public int GetRemaining() => MaxStudents - EnrolledStudentIds.Count;
}
