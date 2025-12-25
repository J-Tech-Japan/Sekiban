using Sekiban.Dcb.Commands;
namespace Dcb.Domain.WithoutResult.Enrollment;

public record DropStudentFromClassRoom(Guid StudentId, Guid ClassRoomId) : ICommand;
