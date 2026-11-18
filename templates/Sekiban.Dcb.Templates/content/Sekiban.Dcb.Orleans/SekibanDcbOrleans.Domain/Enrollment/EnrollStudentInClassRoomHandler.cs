using Dcb.Domain.ClassRoom;
using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Dcb.Domain.Enrollment;

public class EnrollStudentInClassRoomHandler : ICommandHandler<EnrollStudentInClassRoom>
{
    public static Task<ResultBox<EventOrNone>> HandleAsync(EnrollStudentInClassRoom command, ICommandContext context) =>
        ResultBox
            .Start
            .Remap(_ => new StudentTag(command.StudentId))
            .Combine(studentTag => context.TagExistsAsync(studentTag))
            .Verify((_, studentExists) => !studentExists
                ? ExceptionOrNone.FromException(new ApplicationException("Student does not exist"))
                : ExceptionOrNone.None)
            .Remap((studentTag, _) => studentTag)
            .Combine(context.GetStateAsync<StudentState, StudentProjector>)
            .Verify((_, studentState) => studentState.Payload.GetRemaining() <= 0 ?
                ExceptionOrNone.FromException(new ApplicationException("Student has reached maximum class count")) :
                studentState.Payload.EnrolledClassRoomIds.Contains(command.ClassRoomId) ?
                    ExceptionOrNone.FromException(
                        new ApplicationException("Student is already enrolled in this classroom")) :
                    ExceptionOrNone.None)
            .Remap((studentTag, _) => TwoValues.FromValues(studentTag, new ClassRoomTag(command.ClassRoomId)))
            .Combine((_, classRoomTag) => context.GetStateAsync<ClassRoomProjector>(classRoomTag))
            .Verify((_, _, classRoomState) => classRoomState.Payload switch
            {
                EmptyTagStatePayload =>
                    ExceptionOrNone.FromException(new ApplicationException("ClassRoom does not exist")),
                AvailableClassRoomState availableState when availableState.GetRemaining() <= 0 =>
                    ExceptionOrNone.FromException(new ApplicationException("ClassRoom is full")),
                AvailableClassRoomState availableState when availableState.EnrolledStudentIds.Contains(
                        command.StudentId) =>
                    ExceptionOrNone.FromException(
                        new ApplicationException("Student is already enrolled in this classroom")),
                FilledClassRoomState => ExceptionOrNone.FromException(new ApplicationException("ClassRoom is full")),
                _ => ExceptionOrNone.None
            })
            .Conveyor((studentTag, classRoomTag, _) => EventOrNone.EventWithTags(
                new StudentEnrolledInClassRoom(command.StudentId, command.ClassRoomId),
                studentTag,
                classRoomTag));
}
