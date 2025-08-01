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

    public TagState Project(TagState current, Event ev)
    {
        var state = current.Payload as StudentState ?? StudentState.Empty("");
        
        var newState = ev.Payload switch
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

        return current with 
        { 
            Payload = newState,
            Version = current.Version + 1,
            LastSortedUniqueId = int.Parse(ev.SortableUniqueIdValue)
        };
    }
}

public class ClassRoomProjector : ITagProjector
{
    public string GetTagProjectorName() => nameof(ClassRoomProjector);

    public TagState Project(TagState current, Event ev)
    {
        var state = current.Payload as ClassRoomState ?? ClassRoomState.Empty("");
        
        var newState = ev.Payload switch
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

        return current with 
        { 
            Payload = newState,
            Version = current.Version + 1,
            LastSortedUniqueId = int.Parse(ev.SortableUniqueIdValue)
        };
    }
}
// Specialized projector for available seats tracking
public class AvailableClassRoomProjector : ITagProjector
{
    public string GetTagProjectorName() => nameof(AvailableClassRoomProjector);

    public TagState Project(TagState current, Event ev)
    {
        var state = current.Payload as AvailableClassRoomState 
            ?? (current.Payload is EmptyTagStatePayload 
                ? new AvailableClassRoomState("", "", 10, 10) 
                : null);
        
        if (state == null) return current;
        
        var newState = ev.Payload switch
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

        return current with 
        { 
            Payload = newState,
            Version = current.Version + 1,
            LastSortedUniqueId = int.Parse(ev.SortableUniqueIdValue)
        };
    }
}

// Specialized projector for tracking filled classrooms
public class FilledClassRoomProjector : ITagProjector
{
    public string GetTagProjectorName() => nameof(FilledClassRoomProjector);

    public TagState Project(TagState current, Event ev)
    {
        var state = current.Payload as FilledClassRoomState 
            ?? (current.Payload is EmptyTagStatePayload 
                ? new FilledClassRoomState("", "", new List<string>(), false) 
                : null);
        
        if (state == null) return current;
        
        var maxStudents = 10; // Default max students
        
        var newState = ev.Payload switch
        {
            ClassRoomCreated created => new FilledClassRoomState(
                created.ClassRoomId, 
                created.Name,
                new List<string>(),
                false
            ),
            
            StudentEnrolledInClassRoom enrolled => 
            {
                var newEnrolledList = state.EnrolledStudentIds
                    .Concat(new[] { enrolled.StudentId })
                    .Distinct()
                    .ToList();
                    
                return state with 
                { 
                    EnrolledStudentIds = newEnrolledList,
                    IsFull = newEnrolledList.Count >= maxStudents
                };
            },
            
            StudentDroppedFromClassRoom dropped =>
            {
                var newEnrolledList = state.EnrolledStudentIds
                    .Where(id => id \!= dropped.StudentId)
                    .ToList();
                    
                return state with
                {
                    EnrolledStudentIds = newEnrolledList,
                    IsFull = false // No longer full after a drop
                };
            },
            
            _ => state
        };

        return current with 
        { 
            Payload = newState,
            Version = current.Version + 1,
            LastSortedUniqueId = int.Parse(ev.SortableUniqueIdValue)
        };
    }
}
