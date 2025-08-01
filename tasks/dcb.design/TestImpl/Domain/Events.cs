using DcbLib.Events;

namespace Domain;

// DCB Pattern: One command produces ONE event that represents a business fact.
// Events can be tagged with multiple entities to affect their states.

// Entity Creation Events (single entity affected)
public record StudentCreated(string StudentId, string Name) : IEventPayload;

public record ClassRoomCreated(string ClassRoomId, string Name, int MaxStudents = 10) : IEventPayload;

// Business Fact Events (multiple entities affected)
public record StudentEnrolledInClassRoom(string StudentId, string ClassRoomId) : IEventPayload;

public record StudentDroppedFromClassRoom(string StudentId, string ClassRoomId) : IEventPayload;