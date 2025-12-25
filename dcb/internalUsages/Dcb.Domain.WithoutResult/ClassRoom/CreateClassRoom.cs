using Sekiban.Dcb.Commands;
namespace Dcb.Domain.WithoutResult.ClassRoom;

public record CreateClassRoom(Guid ClassRoomId, string Name, int MaxStudents = 10) : ICommand;
