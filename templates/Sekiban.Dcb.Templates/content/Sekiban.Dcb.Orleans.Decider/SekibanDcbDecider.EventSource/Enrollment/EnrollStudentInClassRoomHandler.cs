using Dcb.EventSource.ClassRoom;
using Dcb.EventSource.Student;
using Dcb.ImmutableModels.Events.Enrollment;
using Dcb.ImmutableModels.States.ClassRoom;
using Dcb.ImmutableModels.States.Student;
using Dcb.ImmutableModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using StudentDeciders = Dcb.ImmutableModels.States.Student.Deciders;
using ClassRoomDeciders = Dcb.ImmutableModels.States.ClassRoom.Deciders;
namespace Dcb.EventSource.Enrollment;

public class EnrollStudentInClassRoomHandler : ICommandHandler<EnrollStudentInClassRoom>
{
    public static async Task<EventOrNone> HandleAsync(
        EnrollStudentInClassRoom command,
        ICommandContext context)
    {
        var studentTag = new StudentTag(command.StudentId);
        var studentState = await context.GetStateAsync<StudentState, StudentProjector>(studentTag);

        // Use Decider.Validate for Student (explicit namespace)
        StudentDeciders.StudentEnrolledInClassRoomDecider.Validate(studentState.Payload, command.ClassRoomId);

        var classRoomTag = new ClassRoomTag(command.ClassRoomId);
        var classRoomState = await context.GetStateAsync<ClassRoomProjector>(classRoomTag);

        // Use Decider.Validate for ClassRoom (explicit namespace)
        switch (classRoomState.Payload)
        {
            case AvailableClassRoomState available:
                ClassRoomDeciders.StudentEnrolledInClassRoomDecider.Validate(available, command.StudentId);
                break;
            case FilledClassRoomState filled:
                ClassRoomDeciders.StudentEnrolledInClassRoomDecider.Validate(filled, command.StudentId);
                break;
        }

        return new StudentEnrolledInClassRoom(command.StudentId, command.ClassRoomId)
            .GetEventWithTags();
    }
}
