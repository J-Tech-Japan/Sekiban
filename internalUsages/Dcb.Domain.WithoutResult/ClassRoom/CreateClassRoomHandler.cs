using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
namespace Dcb.Domain.WithoutResult.ClassRoom;

public class CreateClassRoomHandler : ICommandHandlerWithoutResult<CreateClassRoom>
{
    public static async Task<EventOrNone> HandleAsync(CreateClassRoom command, ICommandContext context)
    {
        var tag = new ClassRoomTag(command.ClassRoomId);
        var exists = (await context.TagExistsAsync(tag)).UnwrapBox();
        if (exists)
        {
            throw new ApplicationException("ClassRoom Already Exists");
        }

        return EventOrNone.FromValue(
            new ClassRoomCreated(command.ClassRoomId, command.Name, command.MaxStudents),
            tag);
    }
}
