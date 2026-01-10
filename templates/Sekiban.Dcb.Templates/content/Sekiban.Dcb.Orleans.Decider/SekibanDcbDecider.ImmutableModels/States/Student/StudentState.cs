using Sekiban.Dcb.Tags;
namespace Dcb.ImmutableModels.States.Student;

public record StudentState(Guid StudentId, string Name, int MaxClassCount, List<Guid> EnrolledClassRoomIds)
    : ITagStatePayload
{
    public int GetRemaining() => MaxClassCount - EnrolledClassRoomIds.Count;

    public static StudentState Empty => new(Guid.Empty, string.Empty, 0, []);
}
