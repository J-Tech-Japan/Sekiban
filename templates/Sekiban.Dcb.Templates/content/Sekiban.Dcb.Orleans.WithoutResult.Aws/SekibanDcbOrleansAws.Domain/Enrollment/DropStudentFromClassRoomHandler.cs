using Dcb.Domain.WithoutResult.ClassRoom;
using Dcb.Domain.WithoutResult.Student;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.WithoutResult.Enrollment;

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

        if (!studentState.Payload.EnrolledClassRoomIds.Contains(command.ClassRoomId))
        {
            throw new ApplicationException("Student is not enrolled in this classroom");
        }

        var classRoomTag = new ClassRoomTag(command.ClassRoomId);
        var classRoomState = await context.GetStateAsync<ClassRoomProjector>(classRoomTag);

        var isEnrolled = classRoomState.Payload switch
        {
            EmptyTagStatePayload => throw new ApplicationException("ClassRoom does not exist"),
            AvailableClassRoomState available => available.EnrolledStudentIds.Contains(command.StudentId),
            FilledClassRoomState filled => filled.EnrolledStudentIds.Contains(command.StudentId),
            _ => false
        };

        if (!isEnrolled)
        {
            throw new ApplicationException("Student is not enrolled in this classroom");
        }

        return EventOrNone.From(
            new StudentDroppedFromClassRoom(command.StudentId, command.ClassRoomId),
            studentTag,
            classRoomTag);
    }
}
