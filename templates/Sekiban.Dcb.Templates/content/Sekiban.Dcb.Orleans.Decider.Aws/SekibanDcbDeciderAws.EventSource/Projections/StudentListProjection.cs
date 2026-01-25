using Dcb.ImmutableModels.Events.Enrollment;
using Dcb.ImmutableModels.Events.Student;
using Dcb.ImmutableModels.States.Student;
using Dcb.ImmutableModels.States.Student.Deciders;
using Dcb.ImmutableModels.Tags;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.Projections;

/// <summary>
///     Student list projection for multi-projection queries
/// </summary>
public record StudentListProjection : IMultiProjector<StudentListProjection>
{
    public Dictionary<Guid, StudentState> Students { get; init; } = [];

    public static string MultiProjectorName => nameof(StudentListProjection);
    public static string MultiProjectorVersion => "1.0.0";

    public static StudentListProjection GenerateInitialPayload() => new();

    public static StudentListProjection Project(
        StudentListProjection payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        var studentTags = tags.OfType<StudentTag>().ToList();
        if (studentTags.Count == 0) return payload;

        var updatedStudents = new Dictionary<Guid, StudentState>(payload.Students);

        foreach (var tag in studentTags)
        {
            var studentId = tag.StudentId;
            var currentState = updatedStudents.TryGetValue(studentId, out var existing)
                ? existing
                : StudentState.Empty;

            var newState = ev.Payload switch
            {
                StudentCreated created => StudentCreatedDecider.Create(created),
                StudentEnrolledInClassRoom enrolled => currentState.Evolve(enrolled),
                StudentDroppedFromClassRoom dropped => currentState.Evolve(dropped),
                _ => currentState
            };

            if (newState.StudentId != Guid.Empty)
            {
                updatedStudents[studentId] = newState;
            }
        }

        return payload with { Students = updatedStudents };
    }

    /// <summary>
    ///     Get all students
    /// </summary>
    public IReadOnlyList<StudentState> GetAllStudents() =>
        [.. Students.Values.Where(s => s.StudentId != Guid.Empty)
            .OrderBy(s => s.Name, StringComparer.Ordinal)];
}
