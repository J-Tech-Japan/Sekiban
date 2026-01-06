using Sekiban.Dcb.Commands;
namespace Dcb.Domain.Decider.Enrollment;

public record DropStudentFromClassRoom(Guid StudentId, Guid ClassRoomId) : ICommand;
