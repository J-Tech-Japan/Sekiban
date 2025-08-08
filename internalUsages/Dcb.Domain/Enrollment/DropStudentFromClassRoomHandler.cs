using Dcb.Domain.ClassRoom;
using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
namespace Dcb.Domain.Enrollment;

public class DropStudentFromClassRoomHandler : ICommandHandler<DropStudentFromClassRoom>
{
    public Task<ResultBox<EventOrNone>> HandleAsync(DropStudentFromClassRoom command, ICommandContext context) =>
        ResultBox
            .Start
            .Remap(_ => new StudentTag(command.StudentId))
            .Combine(studentTag => context.GetStateAsync<StudentState, StudentProjector>(studentTag))
            .Verify((_, studentState) => !studentState.Payload.EnrolledClassRoomIds.Contains(command.ClassRoomId)
                ? ExceptionOrNone.FromException(new ApplicationException("Student is not enrolled in this classroom"))
                : ExceptionOrNone.None)
            .Remap((studentTag, studentState) => new ClassRoomTag(command.ClassRoomId))
            .Combine(classRoomTag => context.TagExistsAsync(classRoomTag))
            .Verify((_, classRoomExistsResult) => !classRoomExistsResult
                ? ExceptionOrNone.FromException(new ApplicationException("ClassRoom not found"))
                : ExceptionOrNone.None)
            .Conveyor((classRoomTag, _) => EventOrNone.EventWithTags(
                new StudentDroppedFromClassRoom(command.StudentId, command.ClassRoomId),
                new StudentTag(command.StudentId),
                classRoomTag));
}
