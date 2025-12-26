using Dcb.Domain.WithoutResult.Enrollment;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.WithoutResult.ClassRoom;

public class ClassRoomProjector : ITagProjector<ClassRoomProjector>
{
    public static string ProjectorVersion => "1.0.0";
    public static string ProjectorName => nameof(ClassRoomProjector);

    public static ITagStatePayload Project(ITagStatePayload current, Event ev) =>
        (current, ev.Payload) switch
        {
            (EmptyTagStatePayload, ClassRoomCreated created) => new AvailableClassRoomState(
                created.ClassRoomId,
                created.Name,
                created.MaxStudents,
                new List<Guid>()),

            (AvailableClassRoomState state, StudentEnrolledInClassRoom enrolled) when state.GetRemaining() > 1 => state
                with
            {
                EnrolledStudentIds
                    = state.EnrolledStudentIds.Concat(new[] { enrolled.StudentId }).Distinct().ToList()
            },

            (AvailableClassRoomState state, StudentEnrolledInClassRoom enrolled) when state.GetRemaining() == 1 => new
                FilledClassRoomState(
                    state.ClassRoomId,
                    state.Name,
                    state.EnrolledStudentIds.Concat(new[] { enrolled.StudentId }).Distinct().ToList(),
                    true),

            (AvailableClassRoomState state, StudentDroppedFromClassRoom dropped) => state with
            {
                EnrolledStudentIds = state.EnrolledStudentIds.Where(id => id != dropped.StudentId).ToList()
            },

            (FilledClassRoomState state, StudentDroppedFromClassRoom dropped) => new AvailableClassRoomState(
                state.ClassRoomId,
                state.Name,
                state.EnrolledStudentIds.Count,
                state.EnrolledStudentIds.Where(id => id != dropped.StudentId).ToList()),

            _ => current
        };
}
