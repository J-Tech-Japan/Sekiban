using Sekiban.Dcb.Events;
namespace Dcb.Domain.WithoutResult.ClassRoom;

public record ClassRoomCreated(Guid ClassRoomId, string Name, int MaxStudents = 10) : IEventPayload;
