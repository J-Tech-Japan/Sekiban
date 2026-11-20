using Sekiban.Dcb.Commands;
using System.ComponentModel.DataAnnotations;
namespace Dcb.Domain.ClassRoom;

public record CreateClassRoom(
    [property: Required(ErrorMessage = "ClassRoomId is required")]
    Guid ClassRoomId,
    [property: Required(ErrorMessage = "Name is required")]
    [property: StringLength(100, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 100 characters")]
    string Name,
    [property: Range(1, 100, ErrorMessage = "MaxStudents must be between 1 and 100")]
    int MaxStudents = 10) : ICommand;
