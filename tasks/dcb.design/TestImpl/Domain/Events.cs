using DcbLib.Events;

namespace Domain;

// DCB Pattern: One command produces ONE event that represents a business fact.
// Events can be tagged with multiple entities to affect their states.

// Entity Creation Events (single entity affected)
public record StudentCreated(Guid StudentId, string Name, int MaxClassCount = 5) : IEventPayload;

public record ClassRoomCreated(Guid ClassRoomId, string Name, int MaxStudents = 10) : IEventPayload;

// Business Fact Events (multiple entities affected)
public record StudentEnrolledInClassRoom(Guid StudentId, Guid ClassRoomId) : IEventPayload;

public record StudentDroppedFromClassRoom(Guid StudentId, Guid ClassRoomId) : IEventPayload;