using Sekiban.Dcb.Commands;
namespace Dcb.EventSource.Enrollment;

public record DropStudentFromClassRoom(Guid StudentId, Guid ClassRoomId) : ICommand;
