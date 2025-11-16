using Dcb.Domain.WithoutResult.ClassRoom;
using Dcb.Domain.WithoutResult.Student;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
namespace Dcb.Domain.WithoutResult.Enrollment;

public class DropStudentFromClassRoomHandler : ICommandHandlerWithoutResult<DropStudentFromClassRoom>
{
    public static async Task<EventOrNone> HandleAsync(
        DropStudentFromClassRoom command,
        ICommandContextWithoutResult context)
    {
        var studentTag = new StudentTag(command.StudentId);
        var studentState = await context.GetStateAsync<StudentState, StudentProjector>(studentTag);

        if (!studentState.Payload.EnrolledClassRoomIds.Contains(command.ClassRoomId))
        {
            throw new ApplicationException("Student is not enrolled in this classroom");
        }

        var classRoomTag = new ClassRoomTag(command.ClassRoomId);
        var classRoomState = await context.GetStateAsync<ClassRoomProjector>(classRoomTag);

        var isEnrolled = classRoomState.Payload switch
        {
            AvailableClassRoomState available => available.EnrolledStudentIds.Contains(command.StudentId),
            FilledClassRoomState filled => filled.EnrolledStudentIds.Contains(command.StudentId),
            _ => false
        };

        if (!isEnrolled)
        {
            throw new ApplicationException("Student is not enrolled in this classroom");
        }

        return EventOrNone.FromValue(
            new StudentDroppedFromClassRoom(command.StudentId, command.ClassRoomId),
            studentTag,
            classRoomTag);
    }
}
