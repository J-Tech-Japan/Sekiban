using Sekiban.Dcb.Commands;
namespace Dcb.Domain.WithoutResult.Enrollment;

public record EnrollStudentInClassRoom(Guid StudentId, Guid ClassRoomId) : ICommand;
