using Sekiban.Dcb.Commands;
namespace Dcb.Domain.Enrollment;

public record DropStudentFromClassRoom(Guid StudentId, Guid ClassRoomId) : ICommand;