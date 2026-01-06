using Dcb.ImmutableModels.Events.ClassRoom;
using Dcb.ImmutableModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
namespace Dcb.Domain.Decider.ClassRoom;

public class CreateClassRoomHandler : ICommandHandler<CreateClassRoom>
{
    public static async Task<EventOrNone> HandleAsync(
        CreateClassRoom command,
        ICommandContext context)
    {
        var tag = new ClassRoomTag(command.ClassRoomId);
        var exists = await context.TagExistsAsync(tag);
        if (exists)
        {
            throw new ApplicationException("ClassRoom Already Exists");
        }

        return new ClassRoomCreated(command.ClassRoomId, command.Name, command.MaxStudents)
            .GetEventWithTags();
    }
}
