using Sekiban.Dcb.Commands;
namespace Dcb.Domain.Enrollment;

public record EnrollStudentInClassRoom(Guid StudentId, Guid ClassRoomId) : ICommand;
