using Sekiban.Dcb.Events;
namespace Dcb.Domain.WithoutResult.Enrollment;

public record StudentDroppedFromClassRoom(Guid StudentId, Guid ClassRoomId) : IEventPayload;
