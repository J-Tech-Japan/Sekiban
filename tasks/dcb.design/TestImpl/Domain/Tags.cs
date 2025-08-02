using DcbLib.Events;
using DcbLib.Tags;

namespace Domain;

public record StudentTag(Guid StudentId) : ITag
{
    public bool IsConsistencyTag() => true;
    public string GetTagGroup() => "Student";
    public string GetTag() => $"Student:{StudentId}";
}

public record ClassRoomTag(Guid ClassRoomId) : ITag
{
    public bool IsConsistencyTag() => true;
    public string GetTagGroup() => "ClassRoom";
    public string GetTag() => $"ClassRoom:{ClassRoomId}";
}

public record StudentState(
    Guid StudentId,
    string Name,
    int MaxClassCount,
    List<Guid> EnrolledClassRoomIds
) : ITagStatePayload
{
    public int GetRemaining() => MaxClassCount - EnrolledClassRoomIds.Count;
}

public record AvailableClassRoomState(
    Guid ClassRoomId,
    string Name,
    int MaxStudents,
    List<Guid> EnrolledStudentIds
) : ITagStatePayload
{
    public int GetRemaining() => MaxStudents - EnrolledStudentIds.Count;
}

public record FilledClassRoomState(
    Guid ClassRoomId,
    string Name,
    List<Guid> EnrolledStudentIds,
    bool IsFull
) : ITagStatePayload;

public class StudentProjector : ITagProjector
{
    public string GetTagProjectorName() => nameof(StudentProjector);

    public ITagStatePayload Project(ITagStatePayload current, IEventPayload eventPayload) =>
        (current, eventPayload) switch
        {
            (EmptyTagStatePayload, StudentCreated created) => 
                new StudentState(
                    created.StudentId,
                    created.Name,
                    created.MaxClassCount,
                    new List<Guid>()
                ),
            
            (StudentState state, StudentEnrolledInClassRoom enrolled) when state.GetRemaining() > 0 => 
                state with 
                { 
                    EnrolledClassRoomIds = state.EnrolledClassRoomIds
                        .Concat(new[] { enrolled.ClassRoomId })
                        .Distinct()
                        .ToList()
                },
            
            (StudentState state, StudentDroppedFromClassRoom dropped) =>
                state with
                {
                    EnrolledClassRoomIds = state.EnrolledClassRoomIds
                        .Where(id => id != dropped.ClassRoomId)
                        .ToList()
                },
            
            _ => current
        };
}

public class ClassRoomProjector : ITagProjector
{
    public string GetTagProjectorName() => nameof(ClassRoomProjector);

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
