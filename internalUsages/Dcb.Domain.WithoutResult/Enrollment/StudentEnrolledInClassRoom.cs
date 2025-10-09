using Sekiban.Dcb.Events;
namespace Dcb.Domain.WithoutResult.Enrollment;

public record StudentEnrolledInClassRoom(Guid StudentId, Guid ClassRoomId) : IEventPayload;
