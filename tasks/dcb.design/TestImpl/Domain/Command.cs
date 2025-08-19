using DcbLib.Commands;
using DcbLib.Common;
using DcbLib.Events;
using DcbLib.Tags;
using ResultBoxes;

namespace Domain;

// Commands
public record CreateStudent(Guid StudentId, string Name, int MaxClassCount = 5) 
    : ICommandWithHandler<CreateStudent>
{
    public Task<ResultBox<EventOrNone>> HandleAsync(ICommandContext context)
        => ResultBox.Start.Remap(_ => new StudentTag(StudentId))
            .Combine(tag => context.TagExistsAsync(tag).ToResultBox())
            .Verify((_, exists) =>
                exists
                    ? ExceptionOrNone.FromException(new ApplicationException("Student Already Exists"))
                    : ExceptionOrNone.None)
            .Conveyor((tag, _) =>
                EventOrNone.EventWithTags(new StudentCreated(StudentId, Name, MaxClassCount), tag));
}

public record CreateClassRoom(Guid ClassRoomId, string Name, int MaxStudents = 10) : ICommand;

public record EnrollStudentInClassRoom(Guid StudentId, Guid ClassRoomId) : ICommand;

public record DropStudentFromClassRoom(Guid StudentId, Guid ClassRoomId) : ICommand;


public class CreateClassRoomHandler : ICommandHandler<CreateClassRoom>
{
    public static Task<ResultBox<EventOrNone>> HandleAsync(CreateClassRoom command, ICommandContext context)
        => ResultBox.Start.Remap(_ => new ClassRoomTag(command.ClassRoomId))
            .Combine(tag => context.TagExistsAsync(tag).ToResultBox())
            .Verify((_, exists) =>
                exists
                    ? ExceptionOrNone.FromException(new ApplicationException("ClassRoom Already Exists"))
                    : ExceptionOrNone.None)
            .Conveyor((tag, _) =>
                EventOrNone.EventWithTags(new ClassRoomCreated(command.ClassRoomId, command.Name, command.MaxStudents), tag));
}

public class EnrollStudentInClassRoomHandler : ICommandHandler<EnrollStudentInClassRoom>
{
    public static Task<ResultBox<EventOrNone>> HandleAsync(EnrollStudentInClassRoom command, ICommandContext context)
        => ResultBox.Start
            .Remap(_ => new StudentTag(command.StudentId))
            .Combine(context.GetStateAsync<StudentState, StudentProjector>)
            .Verify((_, studentState) => 
                studentState.Payload.GetRemaining() <= 0
                    ? ExceptionOrNone.FromException(new ApplicationException("Student has reached maximum class count"))
                    : studentState.Payload.EnrolledClassRoomIds.Contains(command.ClassRoomId)
                        ? ExceptionOrNone.FromException(new ApplicationException("Student is already enrolled in this classroom"))
                        : ExceptionOrNone.None)
            .Remap((studentTag, _) => TwoValues.FromValues(studentTag,new ClassRoomTag(command.ClassRoomId)))
            .Combine((_, classRoomTag) => context.GetStateAsync<ClassRoomProjector>(classRoomTag))
            .Verify((_,_, classRoomState) =>
                classRoomState.Payload switch
                {
                    AvailableClassRoomState availableState when availableState.GetRemaining() <= 0 => 
                        ExceptionOrNone.FromException(new ApplicationException("ClassRoom is full")),
                    AvailableClassRoomState availableState when availableState.EnrolledStudentIds.Contains(command.StudentId) => 
                        ExceptionOrNone.FromException(new ApplicationException("Student is already enrolled in this classroom")),
                    FilledClassRoomState => 
                        ExceptionOrNone.FromException(new ApplicationException("ClassRoom is full")),
                    _ => ExceptionOrNone.None
                })
            .Conveyor((studentTag, classRoomTag, _) =>
                EventOrNone.EventWithTags(
                    new StudentEnrolledInClassRoom(command.StudentId, command.ClassRoomId), 
                    studentTag, 
                    classRoomTag));
}

public class DropStudentFromClassRoomHandler : ICommandHandler<DropStudentFromClassRoom>
{
    public static Task<ResultBox<EventOrNone>> HandleAsync(DropStudentFromClassRoom command, ICommandContext context)
        => ResultBox.Start
            .Remap(_ => new StudentTag(command.StudentId))
            .Combine(studentTag => context.GetStateAsync<StudentState, StudentProjector>(studentTag))
            .Verify((_, studentState) =>
                !studentState.Payload.EnrolledClassRoomIds.Contains(command.ClassRoomId)
                    ? ExceptionOrNone.FromException(new ApplicationException("Student is not enrolled in this classroom"))
                    : ExceptionOrNone.None)
            .Remap((studentTag, studentState) => new ClassRoomTag(command.ClassRoomId))
            .Combine(classRoomTag => context.TagExistsAsync(classRoomTag).ToResultBox())
            .Verify((_, classRoomExists) =>
                !classRoomExists
                    ? ExceptionOrNone.FromException(new ApplicationException("ClassRoom not found"))
                    : ExceptionOrNone.None)
            .Conveyor((classRoomTag, _) =>
                EventOrNone.EventWithTags(
                    new StudentDroppedFromClassRoom(command.StudentId, command.ClassRoomId), 
                    new StudentTag(command.StudentId), 
                    classRoomTag));
}