using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
namespace Dcb.Domain.Student;

// Commands
public record CreateStudent(Guid StudentId, string Name, int MaxClassCount = 5) : ICommandWithHandler<CreateStudent>
{
    public Task<ResultBox<EventOrNone>> HandleAsync(ICommandContext context) => ResultBox
        .Start
        .Remap(_ => new StudentTag(StudentId))
        .Combine(tag => context.TagExistsAsync(tag))
        .Verify((_, existsResult) =>
            existsResult
                ? ExceptionOrNone.FromException(new ApplicationException("Student Already Exists"))
                : ExceptionOrNone.None)
        .Conveyor((tag, _) => EventOrNone.EventWithTags(new StudentCreated(StudentId, Name, MaxClassCount), tag));
}
