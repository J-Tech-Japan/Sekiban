using Dcb.ImmutableModels.Events.ClassRoom;
using Dcb.ImmutableModels.Events.Enrollment;
using Dcb.ImmutableModels.States.ClassRoom;
using Dcb.ImmutableModels.States.ClassRoom.Deciders;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.Decider.ClassRoom;

public class ClassRoomProjector : ITagProjector<ClassRoomProjector>
{
    public static string ProjectorVersion => "1.0.0";
    public static string ProjectorName => nameof(ClassRoomProjector);

    public static ITagStatePayload Project(ITagStatePayload current, Event ev) =>
        (current, ev.Payload) switch
        {
            // Create classroom
            (EmptyTagStatePayload, ClassRoomCreated created) => ClassRoomCreatedDecider.Create(created),

            // Enroll student in available classroom
            (AvailableClassRoomState state, StudentEnrolledInClassRoom enrolled) when state.GetRemaining() > 1 =>
                state.Evolve(enrolled),

            // Enroll student in available classroom (becomes filled)
            (AvailableClassRoomState state, StudentEnrolledInClassRoom enrolled) when state.GetRemaining() == 1 =>
                new FilledClassRoomState(
                    state.ClassRoomId,
                    state.Name,
                    state.Evolve(enrolled).EnrolledStudentIds,
                    true),

            // Drop student from available classroom
            (AvailableClassRoomState state, StudentDroppedFromClassRoom dropped) =>
                state.Evolve(dropped),

            // Drop student from filled classroom (becomes available)
            (FilledClassRoomState state, StudentDroppedFromClassRoom dropped) =>
                new AvailableClassRoomState(
                    state.ClassRoomId,
                    state.Name,
                    state.EnrolledStudentIds.Count,
                    state.Evolve(dropped).EnrolledStudentIds),

            _ => current
        };
}
