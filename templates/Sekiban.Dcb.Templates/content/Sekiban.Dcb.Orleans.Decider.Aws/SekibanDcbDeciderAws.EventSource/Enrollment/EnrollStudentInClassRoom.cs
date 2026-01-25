using Sekiban.Dcb.Commands;
namespace Dcb.EventSource.Enrollment;

public record EnrollStudentInClassRoom(Guid StudentId, Guid ClassRoomId) : ICommand;
