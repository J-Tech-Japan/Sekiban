using Dcb.ImmutableModels.Events.Enrollment;
using Dcb.ImmutableModels.Events.Student;
using Dcb.ImmutableModels.States.Student;
using Dcb.ImmutableModels.States.Student.Deciders;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.Decider.Student;

public class StudentProjector : ITagProjector<StudentProjector>
{
    public static string ProjectorVersion => "1.0.0";
    public static string ProjectorName => nameof(StudentProjector);

    public static ITagStatePayload Project(ITagStatePayload current, Event ev) =>
        (current, ev.Payload) switch
        {
            (EmptyTagStatePayload, StudentCreated created) => StudentCreatedDecider.Create(created),
            (StudentState state, StudentEnrolledInClassRoom enrolled) => state.Evolve(enrolled),
            (StudentState state, StudentDroppedFromClassRoom dropped) => state.Evolve(dropped),
            _ => current
        };
}
