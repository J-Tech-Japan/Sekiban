using Dcb.Domain.WithoutResult.ClassRoom;
using Dcb.Domain.WithoutResult.Student;
using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
namespace Dcb.Domain.WithoutResult.Enrollment;


public class EnrollStudentInClassRoomHandler : ICommandHandlerWithoutResult<EnrollStudentInClassRoom>
{
    public static async Task<EventOrNone> HandleAsync(EnrollStudentInClassRoom command, ICommandContext context)
    {
        var studentTag = new StudentTag(command.StudentId);
        var studentState = await context.GetStateAsync<StudentState, StudentProjector>(studentTag).UnwrapBox();

        if (studentState.Payload.GetRemaining() <= 0)
        {
            throw new ApplicationException("Student has reached maximum class count");
        }

        if (studentState.Payload.EnrolledClassRoomIds.Contains(command.ClassRoomId))
        {
            throw new ApplicationException("Student is already enrolled in this classroom");
        }

        var classRoomTag = new ClassRoomTag(command.ClassRoomId);
        var classRoomState = await context.GetStateAsync<ClassRoomProjector>(classRoomTag).UnwrapBox();

        switch (classRoomState.Payload)
        {
            case AvailableClassRoomState available when available.GetRemaining() <= 0:
                throw new ApplicationException("ClassRoom is full");
            case AvailableClassRoomState available when available.EnrolledStudentIds.Contains(command.StudentId):
                throw new ApplicationException("Student is already enrolled in this classroom");
            case FilledClassRoomState:
                throw new ApplicationException("ClassRoom is full");
        }

        return EventOrNone.FromValue(
            new StudentEnrolledInClassRoom(command.StudentId, command.ClassRoomId),
            studentTag,
            classRoomTag);
    }
}
