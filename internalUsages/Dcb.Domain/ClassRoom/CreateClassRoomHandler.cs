using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
namespace Dcb.Domain.ClassRoom;

public class CreateClassRoomHandler : ICommandHandler<CreateClassRoom>
{
    public static Task<ResultBox<EventOrNone>> HandleAsync(CreateClassRoom command, ICommandContext context) => ResultBox
        .Start
        .Remap(_ => new ClassRoomTag(command.ClassRoomId))
        .Combine(tag => context.TagExistsAsync(tag))
        .Verify((_, existsResult) =>
            existsResult
                ? ExceptionOrNone.FromException(new ApplicationException("ClassRoom Already Exists"))
                : ExceptionOrNone.None)
        .Conveyor((tag, _) => EventOrNone.EventWithTags(
            new ClassRoomCreated(command.ClassRoomId, command.Name, command.MaxStudents),
            tag));
}
