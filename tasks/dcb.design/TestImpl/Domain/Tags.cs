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

// ClassRoom states - a classroom is either available or filled
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
        // Handle different state types based on current payload type
        return current switch
        {
            AvailableClassRoomState state => ProjectAvailableClassRoomState(state, eventPayload),
            FilledClassRoomState state => ProjectFilledClassRoomState(state, eventPayload),
            EmptyTagStatePayload => InitializeFromEvent(eventPayload),
            _ => current
        };
    }

    private ITagStatePayload InitializeFromEvent(IEventPayload eventPayload)
    {
        // For empty state, we default to AvailableClassRoomState (new classroom starts with all seats available)
        return eventPayload switch
        {
            ClassRoomCreated created => new AvailableClassRoomState(
                created.ClassRoomId,
                created.Name,
                created.MaxStudents,
                created.MaxStudents // All seats available initially
            ),
            _ => new EmptyTagStatePayload()
        };
    }

    private ITagStatePayload ProjectAvailableClassRoomState(AvailableClassRoomState state, IEventPayload eventPayload)
    {
        switch (eventPayload)
        {
            case ClassRoomCreated created:
                return new AvailableClassRoomState(
                    created.ClassRoomId, 
                    created.Name,
                    created.MaxStudents,
                    created.MaxStudents // Initially all seats are available
                );
                
            case StudentEnrolledInClassRoom enrolled:
                var newAvailableSeats = state.AvailableSeats - 1;
                
                // If no more seats available, transition to FilledClassRoomState
                if (newAvailableSeats <= 0)
                {
                    return new FilledClassRoomState(
                        state.ClassRoomId,
                        state.Name,
                        new List<string> { enrolled.StudentId }, // Start tracking enrolled students
                        true
                    );
                }
                
                return state with { AvailableSeats = newAvailableSeats };
                
            case StudentDroppedFromClassRoom:
                return state with { AvailableSeats = state.AvailableSeats + 1 };
                
            default:
                return state;
        }
    }

    private ITagStatePayload ProjectFilledClassRoomState(FilledClassRoomState state, IEventPayload eventPayload)
    {
        switch (eventPayload)
        {
            case ClassRoomCreated created:
                return new FilledClassRoomState(
                    created.ClassRoomId, 
                    created.Name,
                    new List<string>(),
                    false
                );
            
            case StudentEnrolledInClassRoom enrolled:
                return state with 
                { 
                    EnrolledStudentIds = state.EnrolledStudentIds
                        .Concat(new[] { enrolled.StudentId })
                        .Distinct()
                        .ToList()
                    // Note: IsFull remains true as we're already in FilledClassRoomState
                };
            
            case StudentDroppedFromClassRoom dropped:
                var newEnrolledList = state.EnrolledStudentIds
                    .Where(id => id != dropped.StudentId)
                    .ToList();
                
                // Transition back to AvailableClassRoomState when a seat becomes available
                // We need to know the max students - assuming it's tracked somewhere
                // For now, using a default of 10
                var maxStudents = 10;
                var availableSeats = maxStudents - newEnrolledList.Count;
                
                return new AvailableClassRoomState(
                    state.ClassRoomId,
                    state.Name,
                    maxStudents,
                    availableSeats
                );
            
            default:
                return state;
        }
    }
}
