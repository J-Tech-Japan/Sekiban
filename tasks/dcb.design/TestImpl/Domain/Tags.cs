using DcbLib.Events;
using DcbLib.Tags;

namespace Domain;

// Tag Helper for creating events with proper tags
public static class TagHelper
{
    public static List<ITag> GetTagsForEvent(IEventPayload eventPayload)
    {
        return eventPayload switch
        {
            // Single entity events
            StudentCreated e => new List<ITag> { new StudentTag(e.StudentId) },
            ClassRoomCreated e => new List<ITag> { new ClassRoomTag(e.ClassRoomId) },
            
            // Multi-entity events (DCB pattern - single event affects multiple entities)
            StudentEnrolledInClassRoom e => new List<ITag> 
            { 
                new StudentTag(e.StudentId), 
                new ClassRoomTag(e.ClassRoomId) 
            },
            StudentDroppedFromClassRoom e => new List<ITag> 
            { 
                new StudentTag(e.StudentId), 
                new ClassRoomTag(e.ClassRoomId) 
            },
            
            _ => new List<ITag>()
        };
    }
}

// Tags
public record StudentTag(string StudentId) : ITag
{
    public bool IsConsistencyTag() => true;
    public string GetTagGroup() => "Student";
    public string GetTag() => $"Student:{StudentId}";
}

public record ClassRoomTag(string ClassRoomId) : ITag
{
    public bool IsConsistencyTag() => true;
    public string GetTagGroup() => "ClassRoom";
    public string GetTag() => $"ClassRoom:{ClassRoomId}";
}

// Tag State Payloads
public record StudentState(
    string StudentId,
    string Name,
    List<string> EnrolledClassRoomIds
) : ITagStatePayload
{
    public static StudentState Empty(string studentId) => 
        new(studentId, string.Empty, new List<string>());
}

public record ClassRoomState(
    string ClassRoomId,
    string Name,
    int MaxStudents,
    List<string> EnrolledStudentIds
) : ITagStatePayload
{
    public static ClassRoomState Empty(string classRoomId) => 
        new(classRoomId, string.Empty, 10, new List<string>());
}

// Specialized ClassRoom states for different projections
public record AvailableClassRoomState(
    string ClassRoomId,
    string Name,
    int MaxStudents,
    int AvailableSeats
) : ITagStatePayload;

public record FilledClassRoomState(
    string ClassRoomId,
    string Name,
    List<string> EnrolledStudentIds,
    bool IsFull
) : ITagStatePayload;

// Tag Projectors
public class StudentProjector : ITagProjector
{
    public string GetTagProjectorName() => nameof(StudentProjector);

    public ITagStatePayload Project(ITagStatePayload current, IEventPayload eventPayload)
    {
        var state = current as StudentState ?? StudentState.Empty("");
        
        return eventPayload switch
        {
            StudentCreated created => state with 
            { 
                StudentId = created.StudentId, 
                Name = created.Name 
            },
            
            // Single event affects student state
            StudentEnrolledInClassRoom enrolled => state with 
            { 
                EnrolledClassRoomIds = state.EnrolledClassRoomIds
                    .Concat(new[] { enrolled.ClassRoomId })
                    .Distinct()
                    .ToList()
            },
            
            StudentDroppedFromClassRoom dropped => state with
            {
                EnrolledClassRoomIds = state.EnrolledClassRoomIds
                    .Where(id => id != dropped.ClassRoomId)
                    .ToList()
            },
            
            _ => state
        };
    }
}

public class ClassRoomProjector : ITagProjector
{
    public string GetTagProjectorName() => nameof(ClassRoomProjector);

    public ITagStatePayload Project(ITagStatePayload current, IEventPayload eventPayload)
    {
        var state = current as ClassRoomState ?? ClassRoomState.Empty("");
        
        return eventPayload switch
        {
            ClassRoomCreated created => state with 
            { 
                ClassRoomId = created.ClassRoomId, 
                Name = created.Name,
                MaxStudents = created.MaxStudents
            },
            
            // Same event affects classroom state
            StudentEnrolledInClassRoom enrolled => state with 
            { 
                EnrolledStudentIds = state.EnrolledStudentIds
                    .Concat(new[] { enrolled.StudentId })
                    .Distinct()
                    .ToList()
            },
            
            StudentDroppedFromClassRoom dropped => state with
            {
                EnrolledStudentIds = state.EnrolledStudentIds
                    .Where(id => id != dropped.StudentId)
                    .ToList()
            },
            
            _ => state
        };
    }
}
// Specialized projector for available seats tracking
public class AvailableClassRoomProjector : ITagProjector
{
    public string GetTagProjectorName() => nameof(AvailableClassRoomProjector);

    public ITagStatePayload Project(ITagStatePayload current, IEventPayload eventPayload)
    {
        var state = current as AvailableClassRoomState 
            ?? (current is EmptyTagStatePayload 
                ? new AvailableClassRoomState("", "", 10, 10) 
                : null);
        
        if (state == null) return current;
        
        return eventPayload switch
        {
            ClassRoomCreated created => new AvailableClassRoomState(
                created.ClassRoomId, 
                created.Name,
                created.MaxStudents,
                created.MaxStudents // Initially all seats are available
            ),
            
            StudentEnrolledInClassRoom _ => state with 
            { 
                AvailableSeats = state.AvailableSeats - 1
            },
            
            StudentDroppedFromClassRoom _ => state with
            {
                AvailableSeats = state.AvailableSeats + 1
            },
            
            _ => state
        };
    }
}

// Specialized projector for tracking filled classrooms
public class FilledClassRoomProjector : ITagProjector
{
    public string GetTagProjectorName() => nameof(FilledClassRoomProjector);

    public ITagStatePayload Project(ITagStatePayload current, IEventPayload eventPayload)
    {
        var state = current as FilledClassRoomState 
            ?? (current is EmptyTagStatePayload 
                ? new FilledClassRoomState("", "", new List<string>(), false) 
                : null);
        
        if (state == null) return current;
        
        var maxStudents = 10; // Default max students
        
        return eventPayload switch
        {
            ClassRoomCreated created => new FilledClassRoomState(
                created.ClassRoomId, 
                created.Name,
                new List<string>(),
                false
            ),
            
            StudentEnrolledInClassRoom enrolled => state with 
            { 
                EnrolledStudentIds = state.EnrolledStudentIds
                    .Concat(new[] { enrolled.StudentId })
                    .Distinct()
                    .ToList(),
                IsFull = state.EnrolledStudentIds
                    .Concat(new[] { enrolled.StudentId })
                    .Distinct()
                    .Count() >= maxStudents
            },
            
            StudentDroppedFromClassRoom dropped => state with
            {
                EnrolledStudentIds = state.EnrolledStudentIds
                    .Where(id => id != dropped.StudentId)
                    .ToList(),
                IsFull = false // No longer full after a drop
            },
            
            _ => state
        };
    }
}
