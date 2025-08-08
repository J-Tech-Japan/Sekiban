using Dcb.Domain.Enrollment;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.Student;

public class StudentProjector : ITagProjector
{
    public string GetProjectorVersion() => "1.0.0";

    public ITagStatePayload Project(ITagStatePayload current, IEventPayload eventPayload) =>
        (current, eventPayload) switch
        {
            (EmptyTagStatePayload, StudentCreated created) => new StudentState(
                created.StudentId,
                created.Name,
                created.MaxClassCount,
                new List<Guid>()),

            (StudentState state, StudentEnrolledInClassRoom enrolled) when state.GetRemaining() > 0 => state with
            {
                EnrolledClassRoomIds
                = state.EnrolledClassRoomIds.Concat(new[] { enrolled.ClassRoomId }).Distinct().ToList()
            },

            (StudentState state, StudentDroppedFromClassRoom dropped) => state with
            {
                EnrolledClassRoomIds = state.EnrolledClassRoomIds.Where(id => id != dropped.ClassRoomId).ToList()
            },

            _ => current
        };
}
