using Orleans;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.WithoutResult.Student;

public record StudentState(Guid StudentId, string Name, int MaxClassCount, List<Guid> EnrolledClassRoomIds)
    : ITagStatePayload
{
    public int GetRemaining() => MaxClassCount - EnrolledClassRoomIds.Count;
}
