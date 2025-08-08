using Dcb.Domain.Enrollment;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.ClassRoom;

public class ClassRoomProjector : ITagProjector
{
    /// <summary>
    /// Returns the tag group name this projector targets.
    /// </summary>
    /// <returns>Tag group name.</returns>
    public string ForTagGroupName() => "ClassRoom";

    public string GetProjectorVersion() => "1.0.0";

    public ITagStatePayload Project(ITagStatePayload current, Event ev) =>
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
