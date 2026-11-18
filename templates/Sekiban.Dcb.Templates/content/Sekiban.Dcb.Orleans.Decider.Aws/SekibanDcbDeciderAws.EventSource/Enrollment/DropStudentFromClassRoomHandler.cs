using Dcb.EventSource.ClassRoom;
using Dcb.EventSource.Student;
using Dcb.ImmutableModels.Events.Enrollment;
using Dcb.ImmutableModels.States.ClassRoom;
using Dcb.ImmutableModels.States.Student;
using Dcb.ImmutableModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using StudentDeciders = Dcb.ImmutableModels.States.Student.Deciders;
using ClassRoomDeciders = Dcb.ImmutableModels.States.ClassRoom.Deciders;
namespace Dcb.EventSource.Enrollment;

public class DropStudentFromClassRoomHandler : ICommandHandler<DropStudentFromClassRoom>
{
    public static async Task<EventOrNone> HandleAsync(
        DropStudentFromClassRoom command,
        ICommandContext context)
    {
        var studentTag = new StudentTag(command.StudentId);
        if (!await context.TagExistsAsync(studentTag))
        {
            throw new ApplicationException("Student does not exist");
        }

        var studentState = await context.GetStateAsync<StudentState, StudentProjector>(studentTag);

        // Use Decider.Validate for Student (explicit namespace)
        StudentDeciders.StudentDroppedFromClassRoomDecider.Validate(studentState.Payload, command.ClassRoomId);

        var classRoomTag = new ClassRoomTag(command.ClassRoomId);
        var classRoomState = await context.GetStateAsync<ClassRoomProjector>(classRoomTag);

        // Use Decider.Validate for ClassRoom (explicit namespace)
        switch (classRoomState.Payload)
        {
            case EmptyTagStatePayload:
                throw new ApplicationException("ClassRoom does not exist");
            case AvailableClassRoomState available:
                ClassRoomDeciders.StudentDroppedFromClassRoomDecider.Validate(available, command.StudentId);
                break;
            case FilledClassRoomState filled:
                ClassRoomDeciders.StudentDroppedFromClassRoomDecider.Validate(filled, command.StudentId);
                break;
        }

        return new StudentDroppedFromClassRoom(command.StudentId, command.ClassRoomId)
            .GetEventWithTags();
    }
}
