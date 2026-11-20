using Dcb.Domain.WithoutResult.ClassRoom;
using Dcb.Domain.WithoutResult.Student;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.WithoutResult.Enrollment;


public class EnrollStudentInClassRoomHandler : ICommandHandler<EnrollStudentInClassRoom>
{
    public static async Task<EventOrNone> HandleAsync(
        EnrollStudentInClassRoom command,
        ICommandContext context)
    {
        var studentTag = new StudentTag(command.StudentId);
        if (!await context.TagExistsAsync(studentTag))
        {
            throw new ApplicationException("Student does not exist");
        }

        var studentState = await context.GetStateAsync<StudentState, StudentProjector>(studentTag);

        if (studentState.Payload.GetRemaining() <= 0)
        {
            throw new ApplicationException("Student has reached maximum class count");
        }

        if (studentState.Payload.EnrolledClassRoomIds.Contains(command.ClassRoomId))
        {
            throw new ApplicationException("Student is already enrolled in this classroom");
        }

        var classRoomTag = new ClassRoomTag(command.ClassRoomId);
        var classRoomState = await context.GetStateAsync<ClassRoomProjector>(classRoomTag);

        switch (classRoomState.Payload)
        {
            case EmptyTagStatePayload:
                throw new ApplicationException("ClassRoom does not exist");
            case AvailableClassRoomState available when available.GetRemaining() <= 0:
                throw new ApplicationException("ClassRoom is full");
            case AvailableClassRoomState available when available.EnrolledStudentIds.Contains(command.StudentId):
                throw new ApplicationException("Student is already enrolled in this classroom");
            case FilledClassRoomState:
                throw new ApplicationException("ClassRoom is full");
        }

        return EventOrNone.From(
            new StudentEnrolledInClassRoom(command.StudentId, command.ClassRoomId),
            studentTag,
            classRoomTag);
    }
}
