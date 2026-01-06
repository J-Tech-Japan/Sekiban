using Sekiban.Dcb.Commands;
namespace Dcb.Domain.Decider.Enrollment;

public record EnrollStudentInClassRoom(Guid StudentId, Guid ClassRoomId) : ICommand;
