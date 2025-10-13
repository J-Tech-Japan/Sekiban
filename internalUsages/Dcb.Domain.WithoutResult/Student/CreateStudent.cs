using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.WithoutResult.Student;

// Commands
public record CreateStudent : ICommandWithHandlerWithoutResult<CreateStudent>
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

    public static async Task<EventOrNone> HandleAsync(CreateStudent command, ICommandContext context)
    {
        var tag = new StudentTag(command.StudentId);
        var exists = (await context.TagExistsAsync(tag)).UnwrapBox();
        if (exists)
        {
            throw new ApplicationException("Student Already Exists");
        }

        return EventOrNone.FromValue(new StudentCreated(command.StudentId, command.Name, command.MaxClassCount), tag);
    }
}
