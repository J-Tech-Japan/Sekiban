using Sekiban.Dcb.Events;
namespace Dcb.Domain.Enrollment;

public record StudentDroppedFromClassRoom(Guid StudentId, Guid ClassRoomId) : IEventPayload;
