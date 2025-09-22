using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.Student;

// Commands
public record CreateStudent : ICommandWithHandler<CreateStudent>
{
    [Required(ErrorMessage = "StudentId is required")]
    public Guid StudentId { get; init; }

    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 100 characters")]
    public string Name { get; init; }

    [Range(1, 10, ErrorMessage = "MaxClassCount must be between 1 and 10")]
    public int MaxClassCount { get; init; }

    public CreateStudent(Guid studentId, string name, int maxClassCount = 5)
    {
        StudentId = studentId;
        Name = name;
        MaxClassCount = maxClassCount;
    }

    public static Task<ResultBox<EventOrNone>> HandleAsync(CreateStudent command, ICommandContext context) => ResultBox
        .Start
        .Remap(_ => new StudentTag(command.StudentId))
        .Combine(tag => context.TagExistsAsync(tag))
        .Verify((_, existsResult) =>
            existsResult
                ? ExceptionOrNone.FromException(new ApplicationException("Student Already Exists"))
                : ExceptionOrNone.None)
        .Conveyor((tag, _) => EventOrNone.EventWithTags(new StudentCreated(command.StudentId, command.Name, command.MaxClassCount), tag));
    
}
