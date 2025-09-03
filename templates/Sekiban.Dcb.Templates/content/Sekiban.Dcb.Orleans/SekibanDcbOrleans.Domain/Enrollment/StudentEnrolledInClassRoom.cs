using Sekiban.Dcb.Events;
namespace Dcb.Domain.Enrollment;

public record StudentEnrolledInClassRoom(Guid StudentId, Guid ClassRoomId) : IEventPayload;
