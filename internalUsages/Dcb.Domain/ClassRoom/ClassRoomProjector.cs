using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Dcb.Domain.Enrollment;

namespace Dcb.Domain.ClassRoom;

public class ClassRoomProjector : ITagProjector
{
    public string GetProjectorVersion() => "1.0.0";

    public ITagStatePayload Project(ITagStatePayload current, IEventPayload eventPayload) =>
        (current, eventPayload) switch
        {
            (EmptyTagStatePayload, ClassRoomCreated created) => 
                new AvailableClassRoomState(
                    created.ClassRoomId,
                    created.Name,
                    created.MaxStudents,
                    new List<Guid>()
                ),
            
            (AvailableClassRoomState state, StudentEnrolledInClassRoom enrolled) when state.GetRemaining() > 1 => 
                state with 
                { 
                    EnrolledStudentIds = state.EnrolledStudentIds
                        .Concat(new[] { enrolled.StudentId })
                        .Distinct()
                        .ToList()
                },
                
            (AvailableClassRoomState state, StudentEnrolledInClassRoom enrolled) when state.GetRemaining() == 1 => 
                new FilledClassRoomState(
                    state.ClassRoomId,
                    state.Name,
                    state.EnrolledStudentIds
                        .Concat(new[] { enrolled.StudentId })
                        .Distinct()
                        .ToList(),
                    true
                ),
                
            (AvailableClassRoomState state, StudentDroppedFromClassRoom dropped) => 
                state with 
                { 
                    EnrolledStudentIds = state.EnrolledStudentIds
                        .Where(id => id != dropped.StudentId)
                        .ToList()
                },
            
            (FilledClassRoomState state, StudentDroppedFromClassRoom dropped) => 
                new AvailableClassRoomState(
                    state.ClassRoomId,
                    state.Name,
                    state.EnrolledStudentIds.Count,
                    state.EnrolledStudentIds
                        .Where(id => id != dropped.StudentId)
                        .ToList()
                ),
            
            _ => current
        };
}